using Avalonia.Media.Imaging;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private const int MaxResidentThumbnails = 512;
    private ThumbnailLoadScheduler? _thumbnailScheduler;
    private readonly object _thumbnailSessionsSync = new();
    private readonly HashSet<Task> _thumbnailSessions = new();
    private readonly Dictionary<ImageFile, long> _thumbnailLastAccess = new();
    private int _libraryGeneration;
    private int _requestedThumbnailStart;
    private int _requestedThumbnailCount = ThumbnailConcurrency * 2;
    private long _thumbnailAccessClock;

    public void RequestThumbnailRange(int startIndex, int count)
    {
        _requestedThumbnailStart = Math.Max(0, startIndex);
        _requestedThumbnailCount = Math.Max(1, count);
        QueueRequestedThumbnailRange();
    }

    private void ResetThumbnailViewport()
    {
        _requestedThumbnailStart = 0;
        _requestedThumbnailCount = ThumbnailConcurrency * 2;
        _thumbnailLastAccess.Clear();
        _thumbnailAccessClock = 0;
    }

    private void StartThumbnailSession(
        List<ImageFile> imageFiles,
        CancellationTokenSource requestCts,
        int generation)
    {
        var session = RunThumbnailSessionAsync(imageFiles, requestCts, generation);
        lock (_thumbnailSessionsSync) _thumbnailSessions.Add(session);
        _ = ObserveThumbnailSessionAsync(session);
    }

    private async Task ObserveThumbnailSessionAsync(Task session)
    {
        try
        {
            await session;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Thumbnail session failed: {ex.Message}");
        }
        finally
        {
            lock (_thumbnailSessionsSync) _thumbnailSessions.Remove(session);
        }
    }

    private async Task WaitForThumbnailSessionsAsync()
    {
        Task[] sessions;
        lock (_thumbnailSessionsSync) sessions = _thumbnailSessions.ToArray();
        if (sessions.Length == 0) return;
        await Task.WhenAny(Task.WhenAll(sessions), Task.Delay(TimeSpan.FromSeconds(2)));
    }

    private async Task RunThumbnailSessionAsync(
        List<ImageFile> imageFiles,
        CancellationTokenSource requestCts,
        int generation)
    {
        var cancellationToken = requestCts.Token;
        ThumbnailLoadScheduler? scheduler = null;
        try
        {
            var initialCount = Math.Min(imageFiles.Count, ThumbnailConcurrency * 2);
            await LoadThumbnailRangeAsync(
                imageFiles, 0, initialCount, generation, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            scheduler = new ThumbnailLoadScheduler(
                ThumbnailConcurrency,
                (image, token) => LoadThumbnailAsync(image, generation, token),
                cancellationToken);
            Interlocked.Exchange(ref _thumbnailScheduler, scheduler);
            QueueRequestedThumbnailRange();

            var metadataTask = SweepMetadataAndComputeBurstsAsync(
                imageFiles, cancellationToken);
            await Task.WhenAll(scheduler.Completion, metadataTask);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (scheduler != null)
            {
                Interlocked.CompareExchange(ref _thumbnailScheduler, null, scheduler);
                await scheduler.Completion;
                scheduler.Dispose();
            }

            if (generation != Volatile.Read(ref _libraryGeneration))
            {
                foreach (var image in imageFiles)
                {
                    Library.ReplaceThumbnail(image, null);
                }
            }

            Interlocked.CompareExchange(ref _thumbnailLoadingCts, null, requestCts);
            requestCts.Dispose();
        }
    }

    private async Task LoadThumbnailRangeAsync(
        IReadOnlyList<ImageFile> imageFiles,
        int startIndex,
        int count,
        int generation,
        CancellationToken cancellationToken)
    {
        var nextIndex = startIndex - 1;
        var endIndex = startIndex + count;
        var workerCount = Math.Min(ThumbnailConcurrency, count);
        var workers = Enumerable.Range(0, workerCount).Select(async _ =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var index = Interlocked.Increment(ref nextIndex);
                if (index >= endIndex) return;
                await LoadThumbnailAsync(
                    imageFiles[index], generation, cancellationToken);
            }
        });
        await Task.WhenAll(workers);
    }

    private async Task LoadThumbnailAsync(
        ImageFile imageFile,
        int generation,
        CancellationToken cancellationToken)
    {
        Bitmap? thumbnail = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            imageFile.IsLoading = true;
            thumbnail = await ImageService.LoadThumbnailAsync(imageFile, cancellationToken);
            if (generation != Volatile.Read(ref _libraryGeneration) ||
                !Library.Contains(imageFile))
            {
                return;
            }

            imageFile.ThumbnailLoadFailed = thumbnail == null;
            if (thumbnail != null)
            {
                Library.ReplaceThumbnail(imageFile, thumbnail);
                thumbnail = null;
                if (ReferenceEquals(SelectedImage, imageFile) &&
                    !IsDevelopMode && !IsFullScreenMode)
                {
                    ScheduleHistogramUpdate();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _libraryGeneration) &&
                Library.Contains(imageFile))
            {
                imageFile.ThumbnailLoadFailed = true;
            }
            System.Diagnostics.Debug.WriteLine(
                $"Thumbnail load failed for {imageFile.FilePath}: {ex.Message}");
        }
        finally
        {
            thumbnail?.Dispose();
            imageFile.IsLoading = false;
        }
    }

    private void QueueRequestedThumbnailRange()
    {
        var scheduler = _thumbnailScheduler;
        var images = Library.VisibleImages;
        if (scheduler == null || images.Count == 0) return;

        var visibleStart = Math.Min(_requestedThumbnailStart, images.Count - 1);
        var visibleCount = Math.Min(_requestedThumbnailCount, images.Count - visibleStart);
        var padding = Math.Max(visibleCount * 2, ThumbnailConcurrency * 2);
        var prefetchStart = Math.Max(0, visibleStart - padding);
        var prefetchEnd = Math.Min(images.Count, visibleStart + visibleCount + padding);
        var prefetch = images.Skip(prefetchStart).Take(prefetchEnd - prefetchStart).ToList();
        var visible = images.Skip(visibleStart).Take(visibleCount).ToList();

        foreach (var image in prefetch)
        {
            _thumbnailLastAccess[image] = ++_thumbnailAccessClock;
        }

        ReserveThumbnailResidency(prefetch);
        scheduler.Enqueue(prefetch.Select(image => (image, 1)));
        scheduler.Enqueue(visible.Select(image => (image, 0)));
    }

    private void ReserveThumbnailResidency(IReadOnlyCollection<ImageFile> requested)
    {
        var pinned = new HashSet<ImageFile>(requested, ReferenceEqualityComparer.Instance);
        if (SelectedImage != null) pinned.Add(SelectedImage);
        var residents = Library.AllImages.Where(image => image.Thumbnail != null).ToList();
        var missing = requested.Count(image => image.Thumbnail == null);
        var pinnedResidentCount = pinned.Count(image => image.Thumbnail != null);
        var targetCount = Math.Max(pinnedResidentCount, MaxResidentThumbnails - missing);
        foreach (var image in ThumbnailResidencyPolicy.SelectEvictions(
            residents, pinned, _thumbnailLastAccess, targetCount))
        {
            Library.ReplaceThumbnail(image, null);
        }
    }

    private static async Task CancelAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await cancellation.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cancellation failed: {ex.Message}");
        }
    }
}

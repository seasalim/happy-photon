using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    internal const long ThumbnailPixelBudget = 64L * 1024 * 1024;
    private const long ThumbnailSafetyMargin = 8L * 1024 * 1024;
    private const int MaxPrefetchImages = 128;
    private ThumbnailLoadScheduler? _thumbnailScheduler;
    private readonly object _thumbnailSessionsSync = new();
    private readonly HashSet<Task> _thumbnailSessions = new();
    private readonly Dictionary<ImageFile, long> _thumbnailLastAccess = new();
    private int _browseGeneration;
    private int _thumbnailSizeGeneration;
    private int _requestedThumbnailStart;
    private int _requestedThumbnailCount = ThumbnailConcurrency * 2;
    private long _thumbnailAccessClock;
    private long _peakThumbnailBytes;
    private int _lastQueuedSizeGeneration = -1;
    private readonly ThumbnailPumpAdmissionGate _thumbnailPumpAdmission = new();
    internal Func<Task>? ThumbnailLoadGateAsync { get; set; }
    internal bool IsThumbnailPumpPaused => _thumbnailPumpAdmission.IsPaused;

    public long ResidentThumbnailBytes =>
        Browse.AllImages.Sum(image => image.ThumbnailBytes);
    public long PendingThumbnailRetirementBytes => _bitmapRetirement.PendingBytes;
    public long CombinedThumbnailBytes =>
        ResidentThumbnailBytes + PendingThumbnailRetirementBytes;
    public long PeakThumbnailBytes => Interlocked.Read(ref _peakThumbnailBytes);

    public void RequestThumbnailRange(int startIndex, int count)
    {
        _requestedThumbnailStart = Math.Max(0, startIndex);
        _requestedThumbnailCount = Math.Max(1, count);
        QueueRequestedThumbnailRange();
    }

    private void OnBrowseThumbnailSizeRequestChanged()
    {
        Interlocked.Increment(ref _thumbnailSizeGeneration);
        QueueRequestedThumbnailRange();
    }

    private void ResetThumbnailViewport()
    {
        _requestedThumbnailStart = 0;
        _requestedThumbnailCount = ThumbnailConcurrency * 2;
        _thumbnailLastAccess.Clear();
        _thumbnailAccessClock = 0;
        _lastQueuedSizeGeneration = -1;
    }

    private void StartThumbnailSession(
        List<ImageFile> initialImages,
        List<ImageFile> physicalImages,
        CancellationTokenSource requestCts,
        int generation)
    {
        var request = BrowseThumbnailRequest;
        var sizeGeneration = Volatile.Read(ref _thumbnailSizeGeneration);
        var session = RunThumbnailSessionAsync(
            initialImages,
            physicalImages,
            requestCts,
            generation,
            request,
            sizeGeneration);
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
        List<ImageFile> initialImages,
        List<ImageFile> physicalImages,
        CancellationTokenSource requestCts,
        int generation,
        ThumbnailSizeRequest desiredRequest,
        int sizeGeneration)
    {
        var cancellationToken = requestCts.Token;
        ThumbnailLoadScheduler? scheduler = null;
        try
        {
            var initialCount = Math.Min(initialImages.Count, ThumbnailConcurrency * 2);
            var initialRequest = GetInitialThumbnailRequest(desiredRequest);
            using (initialCount > 0 ? BeginInitialThumbnailBatch() : null)
            {
                await LoadThumbnailRangeAsync(
                    initialImages,
                    0,
                    initialCount,
                    generation,
                    initialRequest,
                    sizeGeneration,
                    cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();

            scheduler = new ThumbnailLoadScheduler(
                ThumbnailConcurrency,
                (image, request, token) => LoadThumbnailAsync(
                    image,
                    generation,
                    request,
                    Volatile.Read(ref _thumbnailSizeGeneration),
                    token),
                cancellationToken);
            Interlocked.Exchange(ref _thumbnailScheduler, scheduler);
            QueueRequestedThumbnailRange();

            await scheduler.Completion;
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

            if (generation != Volatile.Read(ref _browseGeneration))
            {
                foreach (var image in physicalImages)
                {
                    Browse.ReplaceThumbnail(image, null);
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
        ThumbnailSizeRequest request,
        int sizeGeneration,
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
                    imageFiles[index],
                    generation,
                    request,
                    sizeGeneration,
                    cancellationToken);
            }
        });
        await Task.WhenAll(workers);
    }

    private async Task LoadThumbnailAsync(
        ImageFile imageFile,
        int generation,
        CancellationToken cancellationToken)
        => await LoadThumbnailAsync(
            imageFile,
            generation,
            BrowseThumbnailRequest,
            Volatile.Read(ref _thumbnailSizeGeneration),
            cancellationToken);

    private async Task LoadThumbnailAsync(
        ImageFile imageFile,
        int generation,
        ThumbnailSizeRequest request,
        int sizeGeneration,
        CancellationToken cancellationToken)
    {
        Bitmap? thumbnail = null;
        try
        {
            await _thumbnailPumpAdmission.WaitAsync(cancellationToken);
            if (ThumbnailLoadGateAsync is { } gate)
            {
                await gate();
            }
            cancellationToken.ThrowIfCancellationRequested();
            imageFile.IsLoading = true;
            using var result = await ImageService.LoadThumbnailAsync(
                imageFile,
                request,
                allowUndersizedCachePlaceholder: imageFile.Thumbnail == null,
                cancellationToken);
            if (generation != Volatile.Read(ref _browseGeneration) ||
                sizeGeneration != Volatile.Read(ref _thumbnailSizeGeneration) ||
                !Browse.Contains(imageFile))
            {
                return;
            }

            ApplyThumbnailLoadResult(imageFile, result);
            if (result.Status == ThumbnailLoadStatus.Loaded)
            {
                thumbnail = result.DetachBitmap();
                Browse.ReplaceThumbnail(imageFile, thumbnail);
                thumbnail = null;
                UpdateThumbnailMemoryDiagnostics();
                if (!result.SatisfiesMinimumDimension &&
                    !result.BetterResultDeferredForHydration &&
                    !result.SourceCannotProvideRequestedQuality)
                {
                    _thumbnailScheduler?.Enqueue(
                        [new ThumbnailLoadRequest(imageFile, request, 0)],
                        force: true);
                }
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
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (generation == Volatile.Read(ref _browseGeneration) &&
                sizeGeneration == Volatile.Read(ref _thumbnailSizeGeneration) &&
                Browse.Contains(imageFile))
            {
                using var failed = ThumbnailLoadResult.Failed(request);
                ApplyThumbnailLoadResult(imageFile, failed);
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

    private void UpdateThumbnailPumpAdmission()
    {
        var wasPaused = _thumbnailPumpAdmission.IsPaused;
        var pause = IsDevelopMode || IsFullScreenMode || IsCompareMode || IsLoupeMode;
        _thumbnailPumpAdmission.SetPaused(pause);
        if (wasPaused && !pause) SignalBackgroundActivityStarted();
    }

    private void QueueRequestedThumbnailRange()
    {
        var scheduler = _thumbnailScheduler;
        var images = Browse.VisibleImages;
        if (scheduler == null || images.Count == 0) return;

        var visibleStart = Math.Min(_requestedThumbnailStart, images.Count - 1);
        var visibleCount = Math.Min(_requestedThumbnailCount, images.Count - visibleStart);
        var visible = images.Skip(visibleStart).Take(visibleCount).ToList();
        var request = BrowseThumbnailRequest;
        var prefetchCandidates = BuildNearestPrefetch(
            images,
            visibleStart,
            visibleCount);
        var projectedBytes = CombinedThumbnailBytes + visible
            .Where(image => !image.ThumbnailSatisfies(request))
            .Sum(_ => EstimateRequestBytes(request));
        var prefetch = AdmitPrefetch(
            prefetchCandidates,
            request,
            projectedBytes);

        foreach (var image in visible.Concat(prefetch))
        {
            _thumbnailLastAccess[image] = ++_thumbnailAccessClock;
        }

        ReserveThumbnailResidency(visible);
        var requests = prefetch
            .Select(image => new ThumbnailLoadRequest(image, request, 1))
            .Concat(visible.Select(image =>
                new ThumbnailLoadRequest(image, request, 0)))
            .ToList();
        var sizeGeneration = Volatile.Read(ref _thumbnailSizeGeneration);
        if (_lastQueuedSizeGeneration != sizeGeneration)
        {
            _lastQueuedSizeGeneration = sizeGeneration;
            scheduler.ReplaceQueued(requests);
        }
        else
        {
            scheduler.Enqueue(requests);
        }
        // Satisfied thumbnails are discarded by the scheduler; waking the sampler
        // for them would restart its quiet interval with nothing to show.
        if (visible.Concat(prefetch).Any(image =>
                !image.ThumbnailSatisfies(request)))
        {
            SignalBackgroundActivityStarted();
        }
    }

    private void RefreshVisibleThumbnailQueue()
    {
        _lastQueuedSizeGeneration = -1;
        QueueRequestedThumbnailRange();
    }

    private void RetryDeferredThumbnailIfAvailable(ImageFile image)
    {
        if ((image.ThumbnailDeferredForHydration == false &&
             image.ThumbnailUpgradeDeferredDimension == 0) ||
            !ImageService.CanRetryBackgroundRead(image))
        {
            return;
        }

        if (image.ThumbnailDeferredForHydration)
        {
            SetSourceRequiresHydration(image, false);
        }
        image.ThumbnailDeferredForHydration = false;
        image.ThumbnailLoadFailed = false;
        image.ThumbnailUpgradeDeferredDimension = 0;
        image.ThumbnailUpgradeFailedDimension = 0;
        var scheduler = _thumbnailScheduler;
        if (scheduler != null)
        {
            scheduler.Enqueue([
                new ThumbnailLoadRequest(image, BrowseThumbnailRequest, 0)]);
            SignalBackgroundActivityStarted();
        }
    }

    internal void ReserveThumbnailResidency(
        IReadOnlyCollection<ImageFile> requested)
    {
        var pinned = new HashSet<ImageFile>(requested, ReferenceEqualityComparer.Instance);
        if (SelectedImage != null) pinned.Add(SelectedImage);
        var residents = Browse.AllImages.Where(image => image.Thumbnail != null).ToList();
        var pinnedBytes = pinned.Sum(image => image.ThumbnailBytes);
        var targetBytes = Math.Max(
            pinnedBytes,
            ThumbnailPixelBudget - PendingThumbnailRetirementBytes);
        foreach (var image in ThumbnailResidencyPolicy.SelectEvictions(
            residents, pinned, _thumbnailLastAccess, targetBytes))
        {
            Browse.ReplaceThumbnail(image, null);
        }
        UpdateThumbnailMemoryDiagnostics();
    }

    internal static IReadOnlyList<ImageFile> BuildNearestPrefetch(
        IReadOnlyList<ImageFile> images,
        int visibleStart,
        int visibleCount)
    {
        var result = new List<ImageFile>(Math.Min(MaxPrefetchImages, visibleCount * 2));
        var before = visibleStart - 1;
        var after = visibleStart + visibleCount;
        for (var distance = 0;
             distance < visibleCount && result.Count < MaxPrefetchImages;
             distance++)
        {
            if (before >= 0) result.Add(images[before--]);
            if (after < images.Count && result.Count < MaxPrefetchImages)
            {
                result.Add(images[after++]);
            }
        }
        return result;
    }

    internal static IReadOnlyList<ImageFile> AdmitPrefetch(
        IReadOnlyList<ImageFile> candidates,
        ThumbnailSizeRequest request,
        long startingBytes)
    {
        var admitted = new List<ImageFile>();
        var projectedBytes = startingBytes;
        foreach (var image in candidates)
        {
            var requestBytes = image.ThumbnailSatisfies(request)
                ? 0
                : EstimateRequestBytes(request);
            if (admitted.Count >= MaxPrefetchImages ||
                projectedBytes + requestBytes >
                    ThumbnailPixelBudget - ThumbnailSafetyMargin)
            {
                break;
            }

            admitted.Add(image);
            projectedBytes += requestBytes;
        }
        return admitted;
    }

    internal static long EstimateRequestBytes(ThumbnailSizeRequest request)
    {
        var longEdge = request.GenerationDimension;
        return (long)longEdge * (int)Math.Ceiling(longEdge * 2d / 3d) * 4;
    }

    private void UpdateThumbnailMemoryDiagnostics()
    {
        var combined = CombinedThumbnailBytes;
        var current = Interlocked.Read(ref _peakThumbnailBytes);
        while (combined > current)
        {
            var observed = Interlocked.CompareExchange(
                ref _peakThumbnailBytes,
                combined,
                current);
            if (observed == current) break;
            current = observed;
        }

        System.Diagnostics.Debug.WriteLine(
            $"Thumbnail memory: resident={ResidentThumbnailBytes}, " +
            $"pending={PendingThumbnailRetirementBytes}, " +
            $"combined={combined}, peak={PeakThumbnailBytes}");
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

    internal static ThumbnailSizeRequest GetInitialThumbnailRequest(
        ThumbnailSizeRequest desiredRequest) =>
        desiredRequest == ThumbnailSizeRequest.For(BrowseThumbnailSize.Large)
            ? ThumbnailSizeRequest.For(BrowseThumbnailSize.Small)
            : desiredRequest;
}

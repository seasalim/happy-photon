using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.BitmapConversionService;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    public Bitmap? TryPromoteRenderedThumbnail(
        ImageFile imageFile,
        EditSettings settings) =>
        TryPromoteRenderedThumbnail(
            imageFile,
            settings,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium));

    public Bitmap? TryPromoteRenderedThumbnail(
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest request)
    {
        if (!imageFile.IsRaw || !settings.HasEdits) return null;

        Task<Bitmap?> thumbnailTask;
        string hash;
        RenderedPreview rendered;
        lock (_renderedSync)
        {
            var current = _lastRendered;
            if (current == null ||
                !ReferenceEquals(current.ImageFile, imageFile) ||
                current.ThumbnailTask is not { IsCompletedSuccessfully: true })
            {
                return null;
            }

            hash = RenderSettingsHash.Compute(settings);
            if (!string.Equals(current.SettingsHash, hash, StringComparison.Ordinal))
            {
                return null;
            }
            rendered = current;
            thumbnailTask = current.ThumbnailTask;
        }

        var thumbnail = thumbnailTask.GetAwaiter().GetResult();
        if (thumbnail == null) return null;
        Bitmap promoted;
        lock (_renderedSync)
        {
            if (!ReferenceEquals(_lastRendered, rendered)) return null;
            promoted = CloneForRequest(thumbnail, request);
            _renderedThumbnailCache.QueueSaveToCache(imageFile, thumbnail, hash);
        }
        return promoted;
    }

    private Task<Bitmap?>? CreateRenderedThumbnailAsync(
        MagickImage? source,
        int dimension)
    {
        if (source == null) return null;
        var task = Task.Run(() =>
        {
            using (source)
            {
                try
                {
                    RenderColorEncoding.ResizeInLinearLight(source, dimension);
                    var thumbnail = ConvertToBitmap(source);
                    RenderedThumbnailCreated?.Invoke();
                    return thumbnail;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Rendered thumbnail creation failed: {ex.Message}");
                    return null;
                }
            }
        });
        TrackRenderedThumbnailTask(task);
        return task;
    }

    private void QueueRenderedThumbnailWhenReady(RenderedPreview rendered)
    {
        if (rendered.ThumbnailTask == null) return;
        TrackRenderedThumbnailTask(QueueRenderedThumbnailWhenReadyAsync(rendered));
    }

    private async Task QueueRenderedThumbnailWhenReadyAsync(
        RenderedPreview rendered)
    {
        var thumbnail = await rendered.ThumbnailTask!;
        if (thumbnail == null) return;
        try
        {
            _renderedThumbnailCache.QueueSaveToCache(
                rendered.ImageFile,
                thumbnail,
                rendered.SettingsHash);
            if (RenderedThumbnailCacheQueuedAsync is { } cacheQueued)
            {
                await cacheQueued();
            }
        }
        finally
        {
            thumbnail.Dispose();
        }
    }

    private void DisposeRenderedThumbnailWhenReady(RenderedPreview? rendered)
    {
        if (rendered?.ThumbnailTask == null) return;
        TrackRenderedThumbnailTask(
            DisposeRenderedThumbnailWhenReadyAsync(rendered.ThumbnailTask));
    }

    private static async Task DisposeRenderedThumbnailWhenReadyAsync(
        Task<Bitmap?> thumbnailTask)
    {
        var thumbnail = await thumbnailTask;
        thumbnail?.Dispose();
    }

    private void TrackRenderedThumbnailTask(Task task)
    {
        var wake = false;
        lock (_renderedSync)
        {
            wake = _renderedThumbnailTasks.Count == 0;
            _renderedThumbnailTasks.Add(task);
        }
        if (wake) RenderedThumbnailWorkStarted?.Invoke();
        _ = task.ContinueWith(
            completed =>
            {
                lock (_renderedSync) _renderedThumbnailTasks.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task WaitForRenderedThumbnailTasksAsync()
    {
        while (true)
        {
            Task[] tasks;
            lock (_renderedSync) tasks = _renderedThumbnailTasks.ToArray();
            if (tasks.Length == 0) return;
            await Task.WhenAll(tasks);
        }
    }

    private static Bitmap CloneForRequest(
        Bitmap source,
        ThumbnailSizeRequest request)
    {
        if (Math.Max(source.PixelSize.Width, source.PixelSize.Height) <=
            request.GenerationDimension)
        {
            return CloneBitmap(source);
        }

        using var image = ConvertToMagickImage(source);
        ApplyThumbnailSize(image, request.GenerationDimension);
        return ConvertToBitmap(image)!;
    }

    internal WeakReference<Bitmap>? GetRetainedThumbnailReference()
    {
        lock (_renderedSync)
        {
            var task = _lastRendered?.ThumbnailTask;
            if (task is not { IsCompletedSuccessfully: true }) return null;
            var thumbnail = task.GetAwaiter().GetResult();
            return thumbnail == null
                ? null
                : new WeakReference<Bitmap>(thumbnail);
        }
    }
}

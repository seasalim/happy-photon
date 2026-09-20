using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.BitmapConversionService;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    public Task<Bitmap?> TryPromoteRenderedThumbnailAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken = default) =>
        TryPromoteRenderedThumbnailAsync(imageFile, settings,
            ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium), cancellationToken);

    public Task<Bitmap?> TryPromoteRenderedThumbnailAsync(
        ImageFile imageFile, EditSettings settings, ThumbnailSizeRequest request,
        CancellationToken cancellationToken = default) =>
        TrackDisposalTask(() => PromoteRenderedThumbnailAsync(
            imageFile, settings, request, cancellationToken), declineDisposed: true);

    private async Task<Bitmap?> PromoteRenderedThumbnailAsync(
        ImageFile imageFile, EditSettings settings, ThumbnailSizeRequest request,
        CancellationToken cancellationToken)
    {
        if (!imageFile.IsRaw || !settings.HasEdits) return null;
        Bitmap snapshot;
        RenderedPreview rendered;
        var hash = RenderSettingsHash.Compute(settings);
        lock (_renderedSync)
        {
            if (_lastRendered is not { } current ||
                !ReferenceEquals(current.ImageFile, imageFile) ||
                current.SettingsHash != hash ||
                current.ThumbnailTask is not { IsCompletedSuccessfully: true } task ||
                task.Result is not { } thumbnail) return null;
            rendered = current;
            var operation = CullPerf?.Record("CacheEnqueueStart", imageFile.CatalogId, operation: -1) ?? 0;
            try { snapshot = CloneBitmap(thumbnail); }
            finally { CullPerf?.Record("CacheEnqueueEnd", imageFile.CatalogId, operation: operation); }
        }
        var transferred = false;
        try
        {
            return await RunCacheWorkerAsync(imageFile, () =>
            {
                if (Math.Max(snapshot.PixelSize.Width, snapshot.PixelSize.Height) > request.GenerationDimension)
                    CullPerf?.Record("CacheThumbnailResize", imageFile.CatalogId);
                var result = CloneForRequest(snapshot, request);
                _renderedThumbnailCache.QueueSaveToCache(imageFile, snapshot, hash,
                    rendered.Identity?.SourceWriteTime, ownsBitmap: true);
                transferred = true;
                return result;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { if (!transferred) snapshot.Dispose(); }
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
                    return ConvertToBitmap(source);
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

    private async Task NotifyRenderedThumbnailWhenReadyAsync(RenderedPreview rendered)
    {
        if (rendered.ThumbnailTask == null ||
            await rendered.ThumbnailTask.ConfigureAwait(false) == null) return;
        lock (_renderedSync)
            if (ReferenceEquals(_lastRendered, rendered)) RenderedThumbnailCreated?.Invoke();
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
                rendered.SettingsHash,
                rendered.Identity?.SourceWriteTime);
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

    private void DisposeRenderedPreviewWhenReady(RenderedPreview? rendered)
    {
        if (rendered == null) return;
        rendered.DetachStrongBitmap()?.Dispose();
        if (rendered.ThumbnailTask != null)
        {
            TrackRenderedThumbnailTask(
                DisposeRenderedThumbnailWhenReadyAsync(rendered.ThumbnailTask));
        }
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

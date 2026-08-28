using Avalonia;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    private readonly object _disposalSync = new();
    private readonly HashSet<Task> _disposalTasks = new(
        ReferenceEqualityComparer.Instance);

    internal PreviewRenderIdentity? TryGetPreviewRenderIdentity(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return _previewIdentities.TryGetValue(bitmap, out var identity)
            ? identity
            : null;
    }

    internal bool TransferCurrentRenderedBitmap(
        Bitmap bitmap,
        PreviewRenderIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentNullException.ThrowIfNull(identity);
        lock (_renderedSync)
        {
            var current = _lastRendered;
            if (current == null ||
                current.Generation != identity.Generation ||
                !ReferenceEquals(current.ImageFile, identity.ImageFile) ||
                !current.Bitmap.TryGetTarget(out var target) ||
                !ReferenceEquals(target, bitmap))
            {
                return false;
            }

            current.Retain(bitmap);
            return true;
        }
    }

    internal Task<RestingPreview?> RenderRestingPreviewAsync(
        ImageFile imageFile,
        EditSettings settings,
        int fittedLongEdge,
        PreviewRenderIdentity parent,
        CancellationToken cancellationToken) =>
        TrackDisposalTask(() => RenderRestingPreviewCoreAsync(
            imageFile,
            settings,
            fittedLongEdge,
            parent,
            cancellationToken));

    private Task<T> TrackDisposalTask<T>(Func<Task<T>> start,
        bool declineDisposed = false)
    {
        lock (_disposalSync)
        {
            var disposed = Volatile.Read(ref _disposed) != 0;
            if (disposed && declineDisposed) return Task.FromResult(default(T)!);
            ObjectDisposedException.ThrowIf(disposed, this);
            // Started on the caller's thread so its settings snapshot is taken
            // before an edit can race it.
            var task = start();
            _disposalTasks.Add(task);
            _ = task.ContinueWith(
                completed =>
                {
                    lock (_disposalSync) _disposalTasks.Remove(completed);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }

    private async Task<RestingPreview?> RenderRestingPreviewCoreAsync(
        ImageFile imageFile,
        EditSettings settings,
        int fittedLongEdge,
        PreviewRenderIdentity parent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageFile);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(parent);
        if (fittedLongEdge <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fittedLongEdge));
        }

        var settingsSnapshot = settings.Clone();
        var decode = BaseDecodeSettings.From(settingsSnapshot);
        var serial = Interlocked.Increment(ref _restingSerial);
        if (!string.Equals(
                parent.SettingsHash,
                RenderSettingsHash.Compute(settingsSnapshot),
                StringComparison.Ordinal) ||
            !IsCurrentRestingParent(imageFile, decode, parent))
        {
            return null;
        }

        using var large = _baseCoordinator.TryAcquireLargeCurrent(
            imageFile,
            decode);
        if (large == null)
        {
            return null;
        }

        Interlocked.Increment(ref _activeRestingRenders);
        try
        {
            var result = await Task.Run(
                () => RenderRestingCore(
                    large.Base,
                    settingsSnapshot,
                    fittedLongEdge,
                    parent.Generation,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (result == null)
            {
                return null;
            }

            if (serial != Volatile.Read(ref _restingSerial) ||
                !IsCurrentRestingParent(imageFile, decode, parent) ||
                cancellationToken.IsCancellationRequested)
            {
                result.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }
            return result;
        }
        finally
        {
            Interlocked.Decrement(ref _activeRestingRenders);
        }
    }

    private bool TryBeginDispose()
    {
        lock (_disposalSync)
        {
            return Interlocked.Exchange(ref _disposed, 1) == 0;
        }
    }

    private async Task WaitForDisposalTasksAsync()
    {
        DisposalTaskWaitStarted?.Invoke();
        while (true)
        {
            Task[] tasks;
            lock (_disposalSync) tasks = _disposalTasks.ToArray();
            if (tasks.Length == 0) return;
            await Task.WhenAll(tasks)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private RestingPreview? RenderRestingCore(
        BaseImage largeBase,
        EditSettings settings,
        int fittedLongEdge,
        long parentGeneration,
        CancellationToken cancellationToken)
    {
        var execution = RenderExecutionOptions.Resting(
            cancellationToken,
            stageStarted: RestingStageStarted);
        MagickImage? preparedPixels = null;
        try
        {
            RestingStageStarted?.Invoke("snapshot-geometry");
            preparedPixels = RenderGeometry.Apply(
                largeBase.Pixels,
                settings,
                out _);
            execution.ThrowIfCancellationRequested();

            var achievable = checked((int)Math.Max(
                preparedPixels.Width,
                preparedPixels.Height));
            var target = Math.Min(
                Math.Min(fittedLongEdge, achievable),
                BaseImage.LargePreviewMaxDimension);
            if (target <= 0)
            {
                return null;
            }

            RestingStageStarted?.Invoke("snapshot-resize");
            BitmapConversionService.ResizeToMaxDimension(
                preparedPixels,
                target);
            execution.ThrowIfCancellationRequested();

            var preparedSettings = settings.Clone();
            preparedSettings.Rotation = 0;
            preparedSettings.HorizonRotation = 0;
            preparedSettings.Crop = null;
            preparedSettings.Geometry = null;
            using var preparedBase = new BaseImage(
                preparedPixels,
                largeBase.Info);
            preparedPixels = null;

            RestingStageStarted?.Invoke("pipeline");
            using var rendered = _renderPipeline.RenderResting(
                new RenderRequest(
                    preparedBase,
                    preparedSettings,
                    RenderIntent.Preview,
                    target,
                    new RenderOptions(
                        ComputeStats: false,
                        ComputeOverlayMasks: false)),
                execution);
            execution.ThrowIfCancellationRequested();
            RestingStageStarted?.Invoke("bitmap-conversion");
            Avalonia.Media.Imaging.Bitmap? bitmap = null;
            try
            {
                bitmap = BitmapConversionService.ConvertToBitmap(
                    rendered.Image);
                execution.ThrowIfCancellationRequested();
                if (bitmap == null)
                {
                    return null;
                }

                var preview = new RestingPreview(
                    bitmap,
                    parentGeneration,
                    fittedLongEdge,
                    Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                    achievable);
                bitmap = null;
                return preview;
            }
            finally
            {
                bitmap?.Dispose();
            }
        }
        finally
        {
            preparedPixels?.Dispose();
        }
    }

    private bool IsCurrentRestingParent(
        ImageFile imageFile,
        BaseDecodeSettings decode,
        PreviewRenderIdentity parent) =>
        parent.Generation == Volatile.Read(ref _renderGeneration) &&
        ReferenceEquals(parent.ImageFile, imageFile) &&
        string.Equals(parent.DecodeKey, decode.CacheKey, StringComparison.Ordinal);

    private void TagPreview(
        Bitmap? bitmap,
        ImageFile imageFile,
        long generation,
        string decodeKey,
        string settingsHash,
        BaseImage baseImage,
        EditSettings settings)
    {
        if (bitmap == null) return;
        var info = baseImage.Info;
        var originalViewSize = RenderGeometry.CalculateOriginalViewSize(
            info.FullWidth,
            info.FullHeight,
            settings);
        _previewIdentities.Remove(bitmap);
        _previewIdentities.Add(
            bitmap,
            new PreviewRenderIdentity(
                imageFile,
                generation,
                decodeKey,
                settingsHash,
                new PixelSize(info.FullWidth, info.FullHeight),
                originalViewSize));
    }
}

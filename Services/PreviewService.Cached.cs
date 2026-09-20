using HappyPhoton.Models;
using static HappyPhoton.Services.BitmapConversionService;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    private readonly SemaphoreSlim _cacheDecodeGate = new(2, 2);
    internal Func<Task>? CacheDecodeGateAsync { get; set; }

    private async Task<T> RunCacheWorkerAsync<T>(ImageFile imageFile,
        Func<T> work, CancellationToken cancellationToken)
    {
        await _cacheDecodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                if (CacheDecodeGateAsync is { } gate) await gate().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var operation = CullPerf?.Record("CacheDecodeStart", imageFile.CatalogId, operation: -1) ?? 0;
                try { return work(); }
                finally { CullPerf?.Record("CacheDecodeEnd", imageFile.CatalogId, operation: operation); }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _cacheDecodeGate.Release(); }
    }

    public Task<CachedPreviewBitmap?> LoadCachedPreviewAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken = default) =>
        TrackDisposalTask(() => LoadCachedPreviewCoreAsync(
            imageFile,
            settings,
            cancellationToken), declineDisposed: true);

    private async Task<CachedPreviewBitmap?> LoadCachedPreviewCoreAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken)
    {
        var settingsSnapshot = settings.Clone();
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        if (CachedPreviewGateAsync is { } gate)
        {
            await gate().ConfigureAwait(false);
        }
        return await RunCacheWorkerAsync(imageFile, () =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                CullPerf?.Record("CacheStart", imageFile.CatalogId);
                var expectedHash = RenderSettingsHash.Compute(settingsSnapshot);
                using var cached = TryLoadAdjacentWarm(
                        imageFile,
                        expectedHash) ??
                    _previewCache.LoadRenderedPreview(imageFile);
                CullPerf?.Record(cached == null ? "CacheMiss" : "CacheHit", imageFile.CatalogId);
                if (cached == null)
                {
                    return null;
                }

                var settingsMatch = string.Equals(
                    cached.SettingsHash,
                    expectedHash,
                    StringComparison.Ordinal);
                var width = checked((int)cached.Image.Width);
                var height = checked((int)cached.Image.Height);
                var pixels = CopyBgraPixels(cached.Image);
                HistogramData? histogram = null;
                ClippingStats? clipping = null;
                if (settingsMatch)
                {
                    histogram = new HistogramData();
                    HistogramService.CalculatePreviewHistogram(
                        pixels,
                        width,
                        height,
                        histogram,
                        includeWaveform: true);
                    clipping = PreviewCacheService.CalculateDisplayFloorClipping(
                        pixels,
                        width,
                        height);
                }
                CullPerf?.Record("CacheDecoded", imageFile.CatalogId);
                return new CachedPreviewBitmap(
                    ConvertToBitmap(pixels, width, height),
                    settingsMatch,
                    histogram,
                    clipping,
                    cached.OriginalViewPixelSize,
                    cached.OriginalImagePixelSize);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        },
            cancellationToken);
    }
}

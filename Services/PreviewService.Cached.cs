using HappyPhoton.Models;
using static HappyPhoton.Services.BitmapConversionService;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
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
        return await Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var expectedHash = RenderSettingsHash.Compute(settingsSnapshot);
                using var cached = TryLoadAdjacentWarm(
                        imageFile,
                        expectedHash) ??
                    _previewCache.LoadRenderedPreview(imageFile);
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

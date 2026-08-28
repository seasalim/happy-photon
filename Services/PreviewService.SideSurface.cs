using Avalonia;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    public async Task<(Bitmap? Bitmap, PixelSize OriginalViewPixelSize)>
        RenderCurrentBaseSideSurfaceAsync(
            ImageFile image, EditSettings settings, int maxDimension,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(settings);
        var serial = Interlocked.Increment(ref _sideSurfaceSerial);
        var snapshot = settings.Clone();
        var decode = await ResolveDecodeAsync(
            image, snapshot, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (serial != Volatile.Read(ref _sideSurfaceSerial)) return default;
        var useLarge = maxDimension > BaseImage.InteractivePreviewMaxDimension;
        using var interactive = useLarge
            ? null : _baseCoordinator.TryAcquireCurrent(image, decode);
        using var large = useLarge
            ? _baseCoordinator.TryAcquireLargeCurrent(image, decode) : null;
        var source = large?.Base ?? interactive?.Base;
        if (source == null) return default;
        if (SideSurfaceRenderGateAsync is { } gate)
            await gate().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (serial != Volatile.Read(ref _sideSurfaceSerial)) return default;
        var bitmap = await Task.Run(() =>
        {
            using var rendered = _renderPipeline.Render(new RenderRequest(
                source, snapshot, RenderIntent.Preview, maxDimension,
                new RenderOptions(false, false)));
            cancellationToken.ThrowIfCancellationRequested();
            return BitmapConversionService.ConvertToBitmap(rendered.Image);
        }, cancellationToken).ConfigureAwait(false);
        if (bitmap == null) return default;
        if (serial != Volatile.Read(ref _sideSurfaceSerial) ||
            cancellationToken.IsCancellationRequested)
        {
            bitmap.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }
        TagPreview(bitmap, image, serial, decode.CacheKey,
            RenderSettingsHash.Compute(snapshot, source.Info.ProfileToken),
            source, snapshot);
        return (bitmap, RenderGeometry.CalculateOriginalViewSize(
            source.Info.FullWidth, source.Info.FullHeight, snapshot));
    }
}

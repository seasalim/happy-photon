using ImageMagick;

namespace HappyPhoton.Services;

/// <summary>
/// Owns the two preview bases produced by one source decode. The coordinator
/// detaches them into independently retired lease owners.
/// </summary>
public sealed class PreviewBasePair : IDisposable
{
    private BaseImage? _interactive;
    private BaseImage? _large;

    public BaseImage Interactive =>
        _interactive ?? throw new ObjectDisposedException(nameof(PreviewBasePair));

    public BaseImage? Large
    {
        get
        {
            ObjectDisposedException.ThrowIf(_interactive == null, this);
            return _large;
        }
    }

    public PreviewBasePair(BaseImage interactive, BaseImage? large)
    {
        _interactive = interactive ??
            throw new ArgumentNullException(nameof(interactive));
        _large = large;
    }

    internal BaseImage DetachInteractive() =>
        Interlocked.Exchange(ref _interactive, null) ??
        throw new ObjectDisposedException(nameof(PreviewBasePair));

    internal BaseImage? DetachLarge() => Interlocked.Exchange(ref _large, null);

    public void Dispose()
    {
        Interlocked.Exchange(ref _large, null)?.Dispose();
        Interlocked.Exchange(ref _interactive, null)?.Dispose();
    }
}

internal static class PreviewBasePairFactory
{
    internal static PreviewBasePair Create(
        MagickImage decoded,
        BaseImageInfo info,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decoded);
        ArgumentNullException.ThrowIfNull(info);
        cancellationToken.ThrowIfCancellationRequested();

        MagickImage? interactivePixels = null;
        MagickImage? largePixels = null;
        try
        {
            interactivePixels = new MagickImage(decoded);
            BitmapConversionService.ResizeToMaxDimension(
                interactivePixels,
                BaseImage.InteractivePreviewMaxDimension);
            cancellationToken.ThrowIfCancellationRequested();

            largePixels = new MagickImage(decoded);
            BitmapConversionService.ResizeToMaxDimension(
                largePixels,
                BaseImage.LargePreviewMaxDimension);
            cancellationToken.ThrowIfCancellationRequested();

            var interactive = new BaseImage(interactivePixels, info);
            interactivePixels = null;
            var large = new BaseImage(largePixels, info);
            largePixels = null;
            return new PreviewBasePair(interactive, large);
        }
        finally
        {
            largePixels?.Dispose();
            interactivePixels?.Dispose();
        }
    }
}

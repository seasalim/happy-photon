using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public enum RenderIntent
{
    Preview,
    Export
}

[Flags]
public enum ClippingOverlaySide
{
    None = 0,
    SceneHighlights = 1,
    DisplayFloor = 2,
    Both = SceneHighlights | DisplayFloor
}

public sealed record RenderOptions(
    bool ComputeStats = true,
    bool ComputeOverlayMasks = false,
    ClippingOverlaySide OverlaySides = ClippingOverlaySide.Both);

public sealed record RenderRequest(
    BaseImage Base,
    EditSettings Settings,
    RenderIntent Intent,
    int? MaxDimension,
    RenderOptions Options,
    OutputColorSpace OutputColorSpace = OutputColorSpace.Srgb);

public sealed class RenderResult : IDisposable
{
    private MagickImage? _image;
    private ClippingMask? _overlayMask;

    public MagickImage Image =>
        _image ?? throw new ObjectDisposedException(nameof(RenderResult));

    public ClippingStats Clipping { get; }

    public ClippingMask? OverlayMask
    {
        get
        {
            ObjectDisposedException.ThrowIf(_image == null, this);
            return _overlayMask;
        }
    }

    internal RenderResult(
        MagickImage image,
        ClippingStats clipping,
        ClippingMask? overlayMask)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        Clipping = clipping ?? throw new ArgumentNullException(nameof(clipping));
        _overlayMask = overlayMask;
    }

    internal MagickImage DetachImage() =>
        Interlocked.Exchange(ref _image, null) ??
        throw new ObjectDisposedException(nameof(RenderResult));

    internal ClippingMask? DetachOverlayMask()
    {
        ObjectDisposedException.ThrowIf(_image == null, this);
        return Interlocked.Exchange(ref _overlayMask, null);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _overlayMask, null)?.Dispose();
        Interlocked.Exchange(ref _image, null)?.Dispose();
    }
}

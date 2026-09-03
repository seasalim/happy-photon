using Avalonia;
using Avalonia.Controls;

namespace HappyPhoton.Views;

/// <summary>
/// A thumbnail standing in for a larger image. It never draws larger than the
/// original at one device pixel per pixel, matching the Fit cap in ZoomPanControl.
/// </summary>
public sealed class PlaceholderImage : Image
{
    public static readonly StyledProperty<int> OriginalPixelWidthProperty =
        AvaloniaProperty.Register<PlaceholderImage, int>(nameof(OriginalPixelWidth));

    public static readonly StyledProperty<int> OriginalPixelHeightProperty =
        AvaloniaProperty.Register<PlaceholderImage, int>(nameof(OriginalPixelHeight));

    static PlaceholderImage()
    {
        AffectsMeasure<PlaceholderImage>(
            OriginalPixelWidthProperty,
            OriginalPixelHeightProperty);
    }

    public int OriginalPixelWidth
    {
        get => GetValue(OriginalPixelWidthProperty);
        set => SetValue(OriginalPixelWidthProperty, value);
    }

    public int OriginalPixelHeight
    {
        get => GetValue(OriginalPixelHeightProperty);
        set => SetValue(OriginalPixelHeightProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Image);

    protected override Size MeasureOverride(Size availableSize) =>
        base.MeasureOverride(CapToOriginal(availableSize));

    protected override Size ArrangeOverride(Size finalSize) =>
        base.ArrangeOverride(CapToOriginal(finalSize));

    private Size CapToOriginal(Size size)
    {
        if (OriginalPixelWidth <= 0 || OriginalPixelHeight <= 0)
        {
            return size;
        }

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        return new Size(
            Math.Min(size.Width, OriginalPixelWidth / scaling),
            Math.Min(size.Height, OriginalPixelHeight / scaling));
    }
}

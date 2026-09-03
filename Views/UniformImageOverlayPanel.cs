using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HappyPhoton.Views;

public sealed class UniformImageOverlayPanel : Panel
{
    public const double OverlayInset = 4;

    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredSize = default(Size);
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            if (child is Image && child.IsVisible)
            {
                desiredSize = new Size(
                    Math.Max(desiredSize.Width, child.DesiredSize.Width),
                    Math.Max(desiredSize.Height, child.DesiredSize.Height));
            }
        }

        return desiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var shown = Children.OfType<Image>()
            .FirstOrDefault(image => image.IsVisible && image.Source != null);
        // DownOnly means what it does in ZoomPanControl's Fit: never enlarge past
        // one device pixel per image pixel.
        var maxScale = shown?.StretchDirection == StretchDirection.DownOnly
            ? 1 / (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1)
            : double.PositiveInfinity;
        var imageBounds = shown == null
            ? default
            : ViewportRegion.UniformImageBounds(finalSize, shown.Source!.Size, maxScale);
        var panelBounds = shown == null ? new Rect(finalSize) : imageBounds;
        foreach (var image in Children.OfType<Image>())
        {
            image.Arrange(panelBounds);
        }

        foreach (var overlay in Children.Where(child => child is not Image))
        {
            overlay.Arrange(new Rect(
                imageBounds.Left + OverlayInset,
                imageBounds.Bottom - overlay.DesiredSize.Height - OverlayInset,
                overlay.DesiredSize.Width,
                overlay.DesiredSize.Height));
        }

        return finalSize;
    }
}

using Avalonia;
using Avalonia.Controls;

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
        var panelBounds = new Rect(finalSize);
        foreach (var image in Children.OfType<Image>())
        {
            image.Arrange(panelBounds);
        }

        var source = Children.OfType<Image>()
            .FirstOrDefault(image => image.IsVisible && image.Source != null)
            ?.Source;
        var imageBounds = source == null
            ? default
            : ViewportRegion.UniformImageBounds(finalSize, source.Size);

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

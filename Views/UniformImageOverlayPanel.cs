using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace HappyPhoton.Views;

public sealed class UniformImageOverlayPanel : Panel
{
    public const double OverlayInset = 4;

    public static readonly StyledProperty<PixelSize> NativePixelSizeProperty =
        AvaloniaProperty.Register<UniformImageOverlayPanel, PixelSize>(
            nameof(NativePixelSize));

    private TopLevel? _scalingTopLevel;

    static UniformImageOverlayPanel()
    {
        AffectsMeasure<UniformImageOverlayPanel>(NativePixelSizeProperty);
        AffectsArrange<UniformImageOverlayPanel>(NativePixelSizeProperty);
    }

    public PixelSize NativePixelSize
    {
        get => GetValue(NativePixelSizeProperty);
        set => SetValue(NativePixelSizeProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scalingTopLevel = TopLevel.GetTopLevel(this);
        if (_scalingTopLevel != null)
            _scalingTopLevel.ScalingChanged += OnScalingChanged;
        InvalidateMeasure();
        InvalidateArrange();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scalingTopLevel != null)
            _scalingTopLevel.ScalingChanged -= OnScalingChanged;
        _scalingTopLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        InvalidateMeasure();
        InvalidateArrange();
    }

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
        var maxScale = double.PositiveInfinity;
        if (shown?.Source is { } source)
        {
            var nativeSize = shown is PlaceholderImage placeholder
                ? new PixelSize(placeholder.OriginalPixelWidth, placeholder.OriginalPixelHeight)
                : NativePixelSize;
            if (shown is not PlaceholderImage &&
                (nativeSize.Width <= 0 || nativeSize.Height <= 0) && source is Bitmap bitmap)
                nativeSize = bitmap.PixelSize;
            var sourceLongEdge = Math.Max(source.Size.Width, source.Size.Height);
            if (nativeSize.Width > 0 && nativeSize.Height > 0 && sourceLongEdge > 0)
            {
                // Fit is capped by the photograph (or accepted proof), not its proxy.
                maxScale = Math.Max(nativeSize.Width, nativeSize.Height) / sourceLongEdge /
                    (_scalingTopLevel?.RenderScaling ?? 1);
            }
        }
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

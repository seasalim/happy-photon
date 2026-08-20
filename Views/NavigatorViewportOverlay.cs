using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace HappyPhoton.Views;

public sealed class NavigatorViewportOverlay : Control
{
    public const double StrokeThickness = 1;
    public const double HaloThickness = 1;
    public const double CornerRadius = 1;

    public static readonly StyledProperty<Bitmap?> SourceProperty =
        AvaloniaProperty.Register<NavigatorViewportOverlay, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<Rect?> VisibleRegionProperty =
        AvaloniaProperty.Register<NavigatorViewportOverlay, Rect?>(nameof(VisibleRegion));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<NavigatorViewportOverlay, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> HaloProperty =
        AvaloniaProperty.Register<NavigatorViewportOverlay, IBrush?>(nameof(Halo));

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Rect? VisibleRegion
    {
        get => GetValue(VisibleRegionProperty);
        set => SetValue(VisibleRegionProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Halo
    {
        get => GetValue(HaloProperty);
        set => SetValue(HaloProperty, value);
    }

    internal Rect ImageBounds => Source == null
        ? default
        : ViewportRegion.UniformImageBounds(
            Bounds.Size,
            new Size(Source.PixelSize.Width, Source.PixelSize.Height));

    internal Rect? MappedVisibleRegion => VisibleRegion is { } region &&
        ImageBounds is { Width: > 0, Height: > 0 } imageBounds
            ? ViewportRegion.MapToImage(region, imageBounds)
            : null;

    static NavigatorViewportOverlay()
    {
        AffectsRender<NavigatorViewportOverlay>(
            SourceProperty,
            VisibleRegionProperty,
            StrokeProperty,
            HaloProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (MappedVisibleRegion is not { Width: > 0, Height: > 0 } region ||
            Stroke == null ||
            Halo == null)
        {
            return;
        }

        using (context.PushClip(ImageBounds))
        {
            var hairlineRect = region.Deflate(StrokeThickness / 2);
            if (hairlineRect.Width <= 0 || hairlineRect.Height <= 0)
            {
                return;
            }

            context.DrawRectangle(
                null,
                new Pen(Halo, StrokeThickness + HaloThickness * 2),
                hairlineRect,
                CornerRadius,
                CornerRadius);
            context.DrawRectangle(
                null,
                new Pen(Stroke, StrokeThickness),
                hairlineRect,
                CornerRadius,
                CornerRadius);
        }
    }
}

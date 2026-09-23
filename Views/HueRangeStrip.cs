using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Views;

public sealed class HueRangeStrip : Control
{
    public static readonly StyledProperty<double> CenterProperty = AvaloniaProperty.Register<HueRangeStrip, double>(nameof(Center), 240);
    public static readonly StyledProperty<double> RangeWidthProperty = AvaloniaProperty.Register<HueRangeStrip, double>(nameof(RangeWidth), 60);
    public static readonly StyledProperty<double> SoftnessProperty = AvaloniaProperty.Register<HueRangeStrip, double>(nameof(Softness), 30);
    public static readonly StyledProperty<bool> IsSwatchProperty = AvaloniaProperty.Register<HueRangeStrip, bool>(nameof(IsSwatch));
    public double Center { get => GetValue(CenterProperty); set => SetValue(CenterProperty, value); }
    public double RangeWidth { get => GetValue(RangeWidthProperty); set => SetValue(RangeWidthProperty, value); }
    public double Softness { get => GetValue(SoftnessProperty); set => SetValue(SoftnessProperty, value); }
    public bool IsSwatch { get => GetValue(IsSwatchProperty); set => SetValue(IsSwatchProperty, value); }
    static HueRangeStrip() => AffectsRender<HueRangeStrip>(CenterProperty, RangeWidthProperty, SoftnessProperty, IsSwatchProperty);
    public override void Render(DrawingContext context)
    {
        var range = new HueRange { Enabled = true, Center = Center, Width = RangeWidth, Softness = Softness };
        var steps = IsSwatch ? 1 : 180;
        // Center the circular strip on the selection so a window crossing zero stays continuous.
        for (var i = 0; i < steps; i++)
        {
            var hue = IsSwatch ? Center : (Center - 180 + (i + .5) * 360 / steps + 360) % 360;
            var brush = new SolidColorBrush(HappyPhotonColors.GetLocalHueColor(hue));
            var weight = IsSwatch ? 1 : HueWindow.Weight(range, hue, .12);
            using (context.PushOpacity(.2 + .8 * weight))
                context.DrawRectangle(brush, null, new Rect(i * Bounds.Width / steps, 0, Bounds.Width / steps + .5, Bounds.Height));
        }
        if (this.TryFindResource("Divider", ActualThemeVariant, out var frame) && frame is IBrush border)
            context.DrawRectangle(null, new Pen(border, 1), new Rect(Bounds.Size));
        if (!IsSwatch && this.TryFindResource("TextPrimary", ActualThemeVariant, out var ink) && ink is IBrush tick)
            context.DrawLine(new Pen(tick, 2), new Point(Bounds.Width / 2, 0), new Point(Bounds.Width / 2, Bounds.Height / 2));
    }
}

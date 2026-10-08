using Avalonia;
using Avalonia.Controls;

namespace HappyPhoton.Views;

// A scope plot's height follows its width: 80 px at the default 222 px plot width, never less.
public sealed class ScopePlotPanel : Panel
{
    public const double DefaultPlotWidth = 222;

    public const double FloorHeight = 80;

    public static double HeightFor(double width) =>
        double.IsFinite(width)
            ? Math.Max(FloorHeight, Math.Round(FloorHeight * width / DefaultPlotWidth))
            : FloorHeight;

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = HeightFor(availableSize.Width);

        foreach (var child in Children)
        {
            child.Measure(availableSize.WithHeight(height));
        }

        return new Size(0, height);
    }
}

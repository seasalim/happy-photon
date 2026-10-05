using Avalonia;
using Avalonia.Controls;

namespace HappyPhoton.Views;

// Children are the left group, assessment slot (full and compact), and right group.
public sealed class ControlBarLayout : Panel
{
    // Widest full cluster: Rejected, including its active border.
    public const double FullClusterWidth = 366;

    internal Action<double>? PrepareLayout { get; set; }

    private (double Width, double MinimumLeft, double RightWidth) _preparedInputs = (double.NaN, 0, 0);

    private double _leftRoom;

    private double _clusterWidth;

    protected override Size MeasureCore(Size availableSize)
    {
        if (_preparedInputs.Width != availableSize.Width)
        {
            PrepareLayout?.Invoke(availableSize.Width);
        }

        var left = Children[0];
        var right = Children[2];
        right.Measure(Size.Infinity);
        left.Measure(Size.Infinity);
        var minimumLeft = left.MinWidth > 0 ? left.MinWidth : left.DesiredSize.Width;
        var inputs = (availableSize.Width, minimumLeft, right.DesiredSize.Width);

        if (_preparedInputs != inputs)
        {
            _preparedInputs = inputs;
            PrepareBar(availableSize.Width, minimumLeft, right.DesiredSize.Width);
        }

        return base.MeasureCore(availableSize);
    }

    private void PrepareBar(double width, double minimumLeft, double rightWidth)
    {
        var slot = (Grid)Children[1];
        var room = width - minimumLeft - rightWidth;
        var full = slot.Children[0];
        var compact = slot.Children[1];
        full.IsVisible = room >= FullClusterWidth;
        compact.IsVisible = !full.IsVisible && room >= compact.Width;
        _clusterWidth = full.IsVisible ? FullClusterWidth : compact.IsVisible ? compact.Width : 0;
        // Flexible left content yields before the cluster moves off the bar's centre.
        _leftRoom = Math.Max(minimumLeft, Math.Min((width - _clusterWidth) / 2,
            width - rightWidth - _clusterWidth));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var left = Children[0];
        var slot = Children[1];
        var right = Children[2];
        right.Measure(Size.Infinity);
        left.Measure(new Size(_leftRoom, availableSize.Height));
        slot.Measure(new Size(_clusterWidth, availableSize.Height));

        return new Size(left.DesiredSize.Width + slot.DesiredSize.Width + right.DesiredSize.Width,
            Math.Max(left.DesiredSize.Height, Math.Max(slot.DesiredSize.Height, right.DesiredSize.Height)));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var left = Children[0];
        var slot = (Grid)Children[1];
        var right = Children[2];
        var leftWidth = left.DesiredSize.Width;
        var rightX = Math.Max(leftWidth, finalSize.Width - right.DesiredSize.Width);
        var clusterX = Math.Clamp((finalSize.Width - _clusterWidth) / 2, leftWidth,
            Math.Max(leftWidth, rightX - _clusterWidth));
        left.Arrange(new Rect(0, 0, leftWidth, finalSize.Height));
        slot.Arrange(new Rect(clusterX, 0, _clusterWidth, finalSize.Height));
        right.Arrange(new Rect(rightX, 0, right.DesiredSize.Width, finalSize.Height));

        return finalSize;
    }
}

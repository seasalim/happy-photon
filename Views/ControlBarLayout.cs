using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace HappyPhoton.Views;

// Children are the left group, assessment slot (full and compact), and right group.
public sealed class ControlBarLayout : Panel
{
    // Widest full cluster: Rejected, including its active border.
    public const double FullClusterWidth = 366;

    internal Action<double>? PrepareLayout { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        PrepareLayout?.Invoke(availableSize.Width);
        var left = Children[0];
        var slot = (Grid)Children[1];
        var right = Children[2];
        right.Measure(Size.Infinity);
        left.Measure(Size.Infinity);
        var minimumLeft = left.MinWidth > 0 ? left.MinWidth : left.DesiredSize.Width;
        var room = availableSize.Width - minimumLeft - right.DesiredSize.Width;
        var full = slot.Children[0];
        var compact = slot.Children[1];
        full.IsVisible = room >= FullClusterWidth;
        compact.IsVisible = !full.IsVisible && room >= compact.Width;
        var clusterWidth = full.IsVisible ? FullClusterWidth : compact.IsVisible ? compact.Width : 0;
        // Flexible left content yields before the cluster moves off the bar's centre.
        var leftRoom = Math.Max(minimumLeft, Math.Min((availableSize.Width - clusterWidth) / 2,
            availableSize.Width - right.DesiredSize.Width - clusterWidth));
        left.Measure(new Size(leftRoom, availableSize.Height));
        slot.Measure(new Size(Math.Max(0, availableSize.Width - left.DesiredSize.Width -
            right.DesiredSize.Width), availableSize.Height));

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
        var room = Math.Max(0, rightX - leftWidth);

        foreach (var cluster in slot.Children)
        {
            if (!cluster.IsVisible) continue;

            var x = Math.Clamp((finalSize.Width - cluster.Width) / 2, leftWidth,
                Math.Max(leftWidth, rightX - cluster.Width));
            cluster.HorizontalAlignment = HorizontalAlignment.Left;
            cluster.Margin = new Thickness(x - leftWidth, 0, 0, 0);
        }

        slot.Measure(new Size(room, finalSize.Height));
        left.Arrange(new Rect(0, 0, leftWidth, finalSize.Height));
        slot.Arrange(new Rect(leftWidth, 0, room, finalSize.Height));
        right.Arrange(new Rect(rightX, 0, right.DesiredSize.Width, finalSize.Height));

        return finalSize;
    }
}

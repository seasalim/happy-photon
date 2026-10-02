using Avalonia.Controls;

namespace HappyPhoton.Views;

public partial class MainWindow
{
    private void OnShellSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || sender is not Grid grid) return;

        var columns = grid.ColumnDefinitions;
        var left = Math.Clamp(columns[0].Width.Value, columns[0].MinWidth, columns[0].MaxWidth);
        var right = Math.Clamp(columns[4].Width.Value, columns[4].MinWidth, columns[4].MaxWidth);
        var excess = left + right + columns[1].Width.Value + columns[3].Width.Value
            + columns[2].MinWidth - e.NewSize.Width;
        if (excess <= 0) return;

        // Preserve the viewer on shrink, yielding the right pane before the left.
        var rightReduction = Math.Min(excess, Math.Max(0, right - 250));
        var leftReduction = Math.Min(excess - rightReduction, Math.Max(0, left - 240));

        if (rightReduction > 0)
        {
            columns[4].Width = new GridLength(right - rightReduction);
        }

        if (leftReduction > 0)
        {
            columns[0].Width = new GridLength(left - leftReduction);
        }
    }
}

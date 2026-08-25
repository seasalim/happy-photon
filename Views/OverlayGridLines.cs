using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace HappyPhoton.Views;

internal static class OverlayGridLines
{
    public static void Draw(
        Canvas canvas,
        Rect rect,
        IBrush brush,
        int columns,
        int rows)
    {
        for (var column = 1; column < columns; column++)
        {
            var x = rect.Left + rect.Width * column / columns;
            canvas.Children.Add(CreateLine(
                new Point(x, rect.Top),
                new Point(x, rect.Bottom),
                brush));
        }

        for (var row = 1; row < rows; row++)
        {
            var y = rect.Top + rect.Height * row / rows;
            canvas.Children.Add(CreateLine(
                new Point(rect.Left, y),
                new Point(rect.Right, y),
                brush));
        }
    }

    private static Line CreateLine(Point start, Point end, IBrush brush) =>
        new()
        {
            StartPoint = start,
            EndPoint = end,
            Stroke = brush,
            StrokeThickness = 1
        };
}

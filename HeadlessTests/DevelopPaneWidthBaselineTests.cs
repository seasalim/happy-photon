using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// Observation only: keep this workload unchanged when pane constraints change.
public sealed class DevelopPaneWidthBaselineTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(1440, 900, 1)]
    [InlineData(1440, 900, 2)]
    [InlineData(1440, 900, 3)]
    [InlineData(800, 600, 1)]
    [InlineData(800, 600, 2)]
    [InlineData(800, 600, 3)]
    public async Task MeasureViewerFloor(int width, int height, int sample)
    {
        var rows = 0;
        await DevelopToolsBaselineTests.WithScene("normal", width, height, async (_, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns and disposes the supplied MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            Settle(window);
            var splitters = window.GetVisualDescendants().OfType<GridSplitter>()
                .OrderBy(Grid.GetColumn).ToArray();
            var grid = (Grid)splitters[0].Parent!;
            WriteHeader();
            Record("default");
            DragOutward(window, splitters[0], width * 2);
            Record("left outward");
            DragOutward(window, splitters[1], -width * 2);
            Record("right outward");
            await Task.CompletedTask;

            void Record(string stage)
            {
                this.Record(window, grid, splitters, sample, stage);
                rows++;
            }
        });

        Assert.Equal(3, rows);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task MeasureExpandedPanesThenShrink(int sample)
    {
        var rows = 0;
        await DevelopToolsBaselineTests.WithScene("normal", 1440, 900, async (_, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns and disposes the supplied MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            Settle(window);
            var splitters = window.GetVisualDescendants().OfType<GridSplitter>()
                .OrderBy(Grid.GetColumn).ToArray();
            var grid = (Grid)splitters[0].Parent!;
            WriteHeader();
            RecordStage("default");
            DragOutward(window, splitters[0], 400 - grid.ColumnDefinitions[0].ActualWidth);
            RecordStage("left toward 400");
            DragOutward(window, splitters[1], grid.ColumnDefinitions[4].ActualWidth - 450);
            RecordStage("right toward 450");
            window.Width = 800;
            window.Height = 600;
            Settle(window);
            RecordStage("shrink to 800x600");
            await Task.CompletedTask;

            void RecordStage(string stage)
            {
                Record(window, grid, splitters, sample, stage);
                rows++;
            }
        });

        Assert.Equal(4, rows);
    }

    private void WriteHeader()
    {
        output.WriteLine("Sample | Client (DIP) | Stage | Left (DIP) | Right (DIP) | Centre (DIP) | Containment");
        output.WriteLine("Containment uses actual control bounds in client coordinates; overflow is L/T/R/B in DIP.");
    }

    private void Record(Window window, Grid grid, GridSplitter[] splitters, int sample, string stage)
    {
        var left = grid.Children.Single(control => Grid.GetRow(control) == 1 && Grid.GetColumn(control) == 0);
        var right = grid.Children.OfType<DevelopEditPanel>().Single();
        var viewer = window.GetVisualDescendants().OfType<DevelopViewerPane>().Single();
        var containment = string.Join("; ", new[]
        {
            DescribeContainment("left", left),
            DescribeContainment("left splitter", splitters[0]),
            DescribeContainment("viewer", viewer),
            DescribeContainment("right splitter", splitters[1]),
            DescribeContainment("right", right)
        });
        output.WriteLine(FormattableString.Invariant(
            $"{sample} | {window.ClientSize.Width:F2}x{window.ClientSize.Height:F2} | {stage} | {grid.ColumnDefinitions[0].ActualWidth:F2} | {grid.ColumnDefinitions[4].ActualWidth:F2} | {grid.ColumnDefinitions[2].ActualWidth:F2} | {containment}"));

        string DescribeContainment(string name, Control control)
        {
            var bounds = new Rect(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
            var overflowLeft = Math.Max(0, -bounds.Left);
            var overflowTop = Math.Max(0, -bounds.Top);
            var overflowRight = Math.Max(0, bounds.Right - window.ClientSize.Width);
            var overflowBottom = Math.Max(0, bounds.Bottom - window.ClientSize.Height);
            var contained = new Rect(window.ClientSize).Contains(bounds);

            return FormattableString.Invariant(
                $"{name}: {(contained ? "inside" : "outside")} L/T/R/B={overflowLeft:F2}/{overflowTop:F2}/{overflowRight:F2}/{overflowBottom:F2}");
        }
    }

    private void DragOutward(Window window, GridSplitter splitter, double delta)
    {
        var grid = (Grid)splitter.Parent!;
        var column = Grid.GetColumn(splitter);
        var x = grid.ColumnDefinitions.Take(column).Sum(definition => definition.ActualWidth)
            + grid.ColumnDefinitions[column].ActualWidth / 2;
        var y = splitter.Bounds.Y + splitter.Bounds.Height / 2;
        var start = FindPointerTarget(window, splitter,
            grid.TranslatePoint(new Point(x, y), window)!.Value);
        var end = start + new Vector(delta, 0);
        var dragEvents = 0;
        splitter.AddHandler(Thumb.DragDeltaEvent, (_, _) => dragEvents++, handledEventsToo: true);
        window.MouseMove(start, RawInputModifiers.None);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(start + new Vector(Math.Sign(delta), 0), RawInputModifiers.LeftMouseButton);
        Settle(window);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Settle(window);
        window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);
        Settle(window);
        output.WriteLine($"Splitter column={column}: verified pointer target={start}; drag events={dragEvents}");
        Assert.True(dragEvents > 0, "The real pointer drag must reach the splitter.");
    }

    private static Point FindPointerTarget(Window window, GridSplitter splitter, Point centre)
    {
        // Negative margins and rounding make the controls overlap adjacent columns.
        // Select a real hit within the splitter rather than altering the visual tree.
        for (var offset = -3.0; offset <= 3; offset += .25)
        {
            var candidate = centre + new Vector(offset, 0);
            var target = window.InputHitTest(candidate) as Visual;

            if (target == splitter || target?.GetVisualAncestors().Contains(splitter) == true)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The splitter has no reachable pointer target.");
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}

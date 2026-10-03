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

public sealed class ShellPaneLimitsTests
{
    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(800, 600)]
    public async Task SplittersStopAtPaneMaximumsOrViewerMinimum(int width, int height)
    {
        await DevelopToolsBaselineTests.WithScene("normal", width, height, (_, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns the MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            Settle(window);
            var (grid, splitters) = Shell(window);
            Assert.Equal(240, grid.ColumnDefinitions[0].ActualWidth);
            Assert.Equal(250, grid.ColumnDefinitions[4].ActualWidth);
            Drag(window, splitters[0], width * 2);
            AssertContained(window, grid, splitters);
            Drag(window, splitters[1], -width * 2);
            AssertContained(window, grid, splitters);
            var columns = grid.ColumnDefinitions;

            if (width == 1440)
            {
                Assert.Equal(400, columns[0].ActualWidth);
                Assert.Equal(450, columns[4].ActualWidth);
            }
            else
            {
                Assert.Equal(300, columns[2].ActualWidth);
                Assert.InRange(columns[0].ActualWidth, 240, 248);
                Assert.InRange(columns[4].ActualWidth, 250, 258);
                Assert.Equal(498, columns[0].ActualWidth + columns[4].ActualWidth);
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task ShrinkYieldsRightPaneBeforeLeftAndKeepsEverythingInside()
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1440, 900, (_, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns the MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            Settle(window);
            ExpandPanes(window);
            var (grid, splitters) = Shell(window);
            window.Width = 1100;
            Settle(window);
            Assert.Equal(400, grid.ColumnDefinitions[0].ActualWidth);
            Assert.Equal(398, grid.ColumnDefinitions[4].ActualWidth);
            AssertContained(window, grid, splitters);
            window.Width = 800;
            window.Height = 600;
            Settle(window);
            Assert.Equal(250, grid.ColumnDefinitions[4].ActualWidth);
            Assert.Equal(248, grid.ColumnDefinitions[0].ActualWidth);
            AssertContained(window, grid, splitters);
            window.Width = 1440;
            Settle(window);
            ExpandPanes(window);
            window.Width = 800;
            Settle(window);
            AssertContained(window, grid, splitters);

            return Task.CompletedTask;
        });
    }

    internal static void ExpandPanes(Window window)
    {
        Settle(window);
        var (grid, splitters) = Shell(window);
        Drag(window, splitters[0], 3000);
        Drag(window, splitters[1], -3000);
        Assert.Equal(400, grid.ColumnDefinitions[0].ActualWidth);
        Assert.Equal(450, grid.ColumnDefinitions[4].ActualWidth);
    }

    private static (Grid, GridSplitter[]) Shell(Window window)
    {
        var splitters = window.GetVisualDescendants().OfType<GridSplitter>()
            .OrderBy(Grid.GetColumn).ToArray();

        return ((Grid)splitters[0].Parent!, splitters);
    }

    private static void AssertContained(Window window, Grid grid, GridSplitter[] splitters)
    {
        Assert.InRange(grid.ColumnDefinitions[0].ActualWidth, 150, 400);
        Assert.InRange(grid.ColumnDefinitions[4].ActualWidth, 200, 450);
        Assert.True(grid.ColumnDefinitions[2].ActualWidth >= 300);
        Control[] controls = [window.FindControl<Border>("WorkspaceLeftPanel")!, splitters[0],
            window.FindControl<DevelopViewerPane>("DevelopViewerPane")!, splitters[1],
            window.FindControl<DevelopEditPanel>("DevelopEditPanel")!];

        foreach (var control in controls)
        {
            var bounds = new Rect(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
            Assert.True(new Rect(window.ClientSize).Contains(bounds), $"{control.Name}: {bounds}");
        }
    }

    private static void Drag(Window window, GridSplitter splitter, double delta)
    {
        var grid = (Grid)splitter.Parent!;
        var column = Grid.GetColumn(splitter);
        var x = grid.ColumnDefinitions.Take(column).Sum(definition => definition.ActualWidth)
            + grid.ColumnDefinitions[column].ActualWidth / 2;
        var centre = grid.TranslatePoint(new Point(x, splitter.Bounds.Y + splitter.Bounds.Height / 2), window)!.Value;
        var start = Enumerable.Range(0, 25).Select(i => centre + new Vector(-3 + i * .25, 0))
            .First(point => window.InputHitTest(point) is Visual target &&
                (target == splitter || target.GetVisualAncestors().Contains(splitter)));

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

        Assert.True(dragEvents > 0);
    }

    internal static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}

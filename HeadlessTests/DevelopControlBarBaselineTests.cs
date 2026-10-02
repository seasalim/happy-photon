using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopControlBarBaselineTests(ITestOutputHelper output)
{
    // Observation only: retain unchanged to compare G1 before and after the fix.
    [AvaloniaFact]
    public async Task VisibleBoundsCensus()
    {
        var rows = 0;

        await DevelopToolsBaselineTests.WithScene("normal", 1692, 700, async (_, scope) =>
        {
            using var theme = new TestUiScope(theme: ThemeVariant.Dark);
            // test-teardown-policy: allow - WithScene owns the supplied MainWindow scope.
            scope.Show();
            var window = (MainWindow)scope.Window!;
            Settle(window);
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            var workspace = pane.GetVisualAncestors().OfType<Grid>()
                .Single(grid => grid.ColumnDefinitions.Count == 5);
            var left = pane.FindControl<StackPanel>("DevelopImageActionsPanel")!;
            var right = pane.FindControl<StackPanel>("DevelopViewStatePanel")!;
            left.Measure(Size.Infinity);
            right.Measure(Size.Infinity);
            output.WriteLine(FormattableString.Invariant(
                $"NATURAL left={left.DesiredSize.Width:F2} right={right.DesiredSize.Width:F2} DIP"));
            output.WriteLine("case | window DIP | viewer DIP | bar DIP | violations | pairs / outside");

            foreach (var width in new[] { 1200, 597, 596, 595, 500, 409, 408, 407, 308 })
            {
                // Resize the real window, leaving the viewer and default side columns unconstrained.
                window.Width = width + workspace.ColumnDefinitions[0].ActualWidth
                    + workspace.ColumnDefinitions[1].ActualWidth
                    + workspace.ColumnDefinitions[3].ActualWidth
                    + workspace.ColumnDefinitions[4].ActualWidth;
                Settle(window);
                Report($"default-{width}", window, pane);
                rows++;
            }

            // There are no explicit side-column maxima. Exercise the real splitters to their
            // available-space limits, left first and then right, at the minimum window width.
            window.Width = 800;
            Settle(window);
            var splitters = workspace.Children.OfType<GridSplitter>()
                .OrderBy(Grid.GetColumn).ToArray();
            DragToLimit(window, splitters[0], 10000);
            DragToLimit(window, splitters[1], -10000);
            output.WriteLine(FormattableString.Invariant(
                $"MAX-PANES left={workspace.ColumnDefinitions[0].ActualWidth:F2} right={workspace.ColumnDefinitions[4].ActualWidth:F2} DIP; drag order=left,right"));
            Report("max-panes", window, pane);
            rows++;
            await Task.CompletedTask;
        });

        Assert.Equal(10, rows);
    }

    private void Report(string label, MainWindow window, DevelopViewerPane pane)
    {
        var bar = pane.FindControl<Border>("DevelopControlBar")!;
        var right = pane.FindControl<StackPanel>("DevelopViewStatePanel")!;
        var controls = new List<Control> { pane.FindControl<StackPanel>("DevelopImageActionsPanel")! };
        controls.Add(pane.FindControl<Border>("DevelopAssessmentHost")!);
        controls.Add(pane.FindControl<DevelopAssessmentCluster>("DevelopCompactAssessment")!);
        controls.AddRange(right.Children);
        var visible = controls.Where(control => control.IsEffectivelyVisible)
            .Select(control => (Name: Identity(control), Bounds: BoundsIn(control, bar))).ToArray();
        var violations = new List<string>();
        var barBounds = new Rect(bar.Bounds.Size);

        for (var i = 0; i < visible.Length; i++)
        {
            if (!barBounds.Contains(visible[i].Bounds))
            {
                violations.Add($"outside({visible[i].Name})");
            }

            for (var j = i + 1; j < visible.Length; j++)
            {
                if (visible[i].Bounds.Intersects(visible[j].Bounds))
                {
                    violations.Add($"{visible[i].Name}<->{visible[j].Name}");
                }
            }
        }

        output.WriteLine(FormattableString.Invariant(
            $"{label} | {window.Bounds.Width:F2} | {pane.Bounds.Width:F2} | {bar.Bounds.Width:F2} | {violations.Count} | {(violations.Count == 0 ? "none" : string.Join("; ", violations))}"));

        foreach (var control in visible)
        {
            output.WriteLine(FormattableString.Invariant(
                $"  {control.Name}: x={control.Bounds.X:F2}, y={control.Bounds.Y:F2}, w={control.Bounds.Width:F2}, h={control.Bounds.Height:F2}"));
        }
    }

    private void DragToLimit(MainWindow window, GridSplitter splitter, double delta)
    {
        var start = splitter.TranslatePoint(
            new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;

        // The one-pixel template is narrower than the splitter and overlapping pane hit areas.
        for (var x = 0.0; x < splitter.Bounds.Width; x += .25)
        {
            var candidate = splitter.TranslatePoint(new Point(x, splitter.Bounds.Height / 2), window)!.Value;
            var hit = window.InputHitTest(candidate) as Visual;

            if (hit == splitter || hit?.GetVisualAncestors().Contains(splitter) == true)
            {
                start = candidate;
                break;
            }
        }

        output.WriteLine($"DRAG column={Grid.GetColumn(splitter)} start={start} hit={window.InputHitTest(start)?.GetType().Name}");
        window.MouseMove(start);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(Math.Sign(delta) * 10, 0), RawInputModifiers.LeftMouseButton);
        window.MouseMove(start + new Vector(delta, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(delta, 0), MouseButton.Left);
        Settle(window);
    }

    private static string Identity(Control control) => control.Name
        ?? (control is CompactSlider slider ? $"{slider.Label} slider"
            : AutomationProperties.GetName(control) ?? (control as Button)?.Content?.ToString()
                ?? control.GetType().Name);

    private static Rect BoundsIn(Control control, Control parent) =>
        new(control.TranslatePoint(default, parent)!.Value, control.Bounds.Size);

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}

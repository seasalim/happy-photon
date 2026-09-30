using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsHoverBaselineTests
{
    [AvaloniaFact]
    public Task ArmedPressGrid() => WithOverlay((vm, window, viewer, overlay) =>
    {
        var frame = vm.LocalsFrame!.Value;
        Point W(Point p) => overlay.TranslatePoint(Canvas(p, frame, overlay), window)!.Value;

        foreach (var type in new[] { "radial", "linear" })
        {
            foreach (var selected in new[] { "radial", "linear" })
            {
                Reset(vm, selected);
                var grid = Grid(vm.SelectedLocal!, frame);

                if (selected == "radial") grid.Add(("handle-pin-overlap", grid.Single(p => p.Name == "x+").Point));

                foreach (var (name, point) in grid)
                {
                    Reset(vm, selected);

                    if (name == "handle-pin-overlap")
                    {
                        vm.Locals[1].Cu = point.X;
                        vm.Locals[1].Cv = point.Y;
                    }

                    if (type == "radial") vm.AddRadialCommand.Execute(null);
                    else vm.AddLinearCommand.Execute(null);
                    var before = vm.Locals.Select(l => l with { }).ToArray();
                    var start = W(point);
                    var end = start + new Vector(12, 8);
                    window.MouseMove(start);
                    Assert.Equal("Cross", CursorName(overlay));
                    var hover = HoverAction(overlay);
                    window.MouseDown(start, MouseButton.Left);
                    AssertPressParity(hover, vm);
                    var gesture = Gesture(vm);
                    window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                    var changes = Changes(before, vm.Locals);
                    output.WriteLine($"L1 armed-{type}/{selected}/{name}: selected={vm.SelectedLocal?.Type}; gesture={gesture}; changed={changes}");
                    Assert.Equal("Create", gesture);
                    Assert.Equal(type, vm.SelectedLocal?.Type);
                    Assert.DoesNotContain(before, local => local.Id == vm.SelectedLocal!.Id);
                    Assert.Equal("new-" + type, changes);
                    vm.DiscardLocalsGesture();
                    window.MouseUp(end, MouseButton.Left);
                }
            }
        }

        return Task.CompletedTask;
    });
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task ArmedGestureCursor(bool radial, bool cancel) => WithOverlay(async (vm, window, viewer, overlay) =>
    {
        if (radial) vm.AddRadialCommand.Execute(null);
        else vm.AddLinearCommand.Execute(null);
        var start = overlay.TranslatePoint(new Point(60, 60), window)!.Value;
        window.MouseMove(start);
        window.MouseDown(start, MouseButton.Left);
        var cursor = overlay.Cursor;
        Assert.Equal("Cross", CursorName(overlay));
        window.MouseMove(start + new Vector(40, 30), RawInputModifiers.LeftMouseButton);
        Assert.Same(cursor, overlay.Cursor);
        var outside = new Point(-20, -20);
        window.MouseMove(outside, RawInputModifiers.LeftMouseButton);
        Assert.Same(cursor, overlay.Cursor);

        if (cancel) vm.DiscardLocalsGesture();
        else
        {
            window.MouseUp(outside, MouseButton.Left);
            if (vm.PendingHistoryCommitTask is { } commit) await commit.WaitAsync(TestWaits.Condition);
        }

        Assert.False(vm.IsLocalsGestureActive);
        Assert.Equal("Arrow", CursorName(overlay));
        if (cancel) window.MouseUp(outside, MouseButton.Left);
    });
}


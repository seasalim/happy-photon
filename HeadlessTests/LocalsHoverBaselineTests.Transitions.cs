using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsHoverBaselineTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task GestureTransitions(bool outside, bool cancel) => WithOverlay(async (vm, window, viewer, overlay) =>
    {
        var frame = vm.LocalsFrame!.Value;
        Point W(Point p) => overlay.TranslatePoint(p, window)!.Value;
        var capturedExits = 0;
        overlay.PointerExited += (_, e) =>
        {
            if (e.Pointer.Captured == overlay) capturedExits++;
        };

        foreach (var selected in new[] { "radial", "linear" })
        {
            Reset(vm, selected);
            var grid = Grid(vm.SelectedLocal!, frame).Where(p => !p.Name.Contains("pin") && p.Name != "empty").ToArray();

            foreach (var (name, point) in grid)
            {
                Reset(vm, selected);
                var start = W(Canvas(point, frame, overlay));
                var end = outside ? new Point(-20, -20) : start + new Vector(12, 8);
                window.MouseMove(start);
                var hover = CursorName(overlay);
                Assert.Equal(ExpectedCursor(selected, name), hover);
                window.MouseDown(start, MouseButton.Left);
                Assert.True(vm.IsLocalsGestureActive);
                var press = CursorName(overlay);
                window.MouseMove(start + new Vector(12, 8), RawInputModifiers.LeftMouseButton);
                var drag = CursorName(overlay);
                var exitsBefore = capturedExits;
                window.MouseMove(end, RawInputModifiers.LeftMouseButton);
                var exit = CursorName(overlay);

                Assert.Equal(hover, press);
                Assert.Equal(hover, drag);
                Assert.Equal(hover, exit);
                if (outside) Assert.True(capturedExits > exitsBefore);

                if (cancel)
                {
                    Assert.True(overlay.Focus());
                    window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                    window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
                    if (vm.HandleEscapeCommand.ExecutionTask is { } task) await task.WaitAsync(TestWaits.Condition);
                }
                else
                {
                    window.MouseUp(end, MouseButton.Left);
                    if (vm.PendingHistoryCommitTask is { } task) await task.WaitAsync(TestWaits.Condition);
                }

                Assert.False(vm.IsLocalsGestureActive);
                var released = CursorName(overlay);
                window.MouseMove(end);
                Assert.Equal(CursorName(overlay), released);
                if (outside) Assert.Equal("Arrow", released);
                if (cancel) window.MouseUp(end, MouseButton.Left);
                window.MouseMove(new Point(-20, -20));
                Assert.Equal("Arrow", CursorName(overlay));
                output.WriteLine($"L3 {selected}/{name} outside={outside} cancel={cancel}: hover={hover}; press={press}; drag={drag}; exit={exit}; end={released}; idle-exit={CursorName(overlay)}");
            }
        }
    });

    [AvaloniaFact]
    public Task StationaryTransitions() => WithOverlay(async (vm, window, viewer, overlay) =>
    {
        var frame = vm.LocalsFrame!.Value;
        var resting = overlay.TranslatePoint(Canvas(new(.4, .45), frame, overlay), window)!.Value;
        window.MouseMove(resting);
        void Report(string change) => output.WriteLine($"L4 {change}: cursor={CursorName(overlay)}; selected={vm.SelectedLocal?.Type}; armed={vm.IsLocalCreationArmed}; overlay-hit={ReferenceEquals(overlay, window.InputHitTest(resting))}");
        Report("before");
        Assert.Equal("SizeAll", CursorName(overlay));
        vm.SelectedLocal = vm.Locals[1];
        Report("select-linear");
        Assert.Equal("Hand", CursorName(overlay));
        vm.SelectedLocal = vm.Locals[2];
        Report("select-brush");
        Assert.Equal("None", CursorName(overlay));
        vm.SelectedLocal = vm.Locals[0];
        Report("select-radial");
        Assert.Equal("SizeAll", CursorName(overlay));
        vm.AddRadialCommand.Execute(null);
        Report("arm-radial");
        Assert.Equal("Cross", CursorName(overlay));
        vm.DiscardLocalsGesture();
        Report("disarm-radial");
        Assert.Equal("SizeAll", CursorName(overlay));
        vm.AddLinearCommand.Execute(null);
        Report("arm-linear");
        Assert.Equal("Cross", CursorName(overlay));
        vm.DiscardLocalsGesture();
        Report("disarm-linear");
        Assert.Equal("SizeAll", CursorName(overlay));
        vm.SelectedLocal = vm.Locals[0];
        await vm.DeleteLocalCommand.ExecuteAsync(vm.SelectedLocal);
        Assert.Equal(2, vm.Locals.Count);
        Report("delete");
        Assert.Equal("Arrow", CursorName(overlay));
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Locals.Count);
        Report("undo");
        Assert.Equal("linear", vm.SelectedLocal?.Type);
        Assert.Equal("Hand", CursorName(overlay));
        viewer.ZoomLevel = 1;
        Settle();
        Report("zoom");
        AssertStationary();
        viewer.FindControl<ScrollViewer>("ScrollViewer")!.Offset += new Vector(80, 40);
        Settle();
        Report("pan");
        AssertStationary();
        window.Width += 200;
        Settle();
        Report("resize");
        AssertStationary();

        void AssertStationary()
        {
            var stationary = CursorName(overlay);
            window.MouseMove(resting);
            Assert.Equal(CursorName(overlay), stationary);
        }
    });
}


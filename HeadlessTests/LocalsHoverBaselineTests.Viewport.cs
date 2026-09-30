using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsHoverBaselineTests
{
    [AvaloniaTheory]
    [InlineData("zoom")]
    [InlineData("pan")]
    [InlineData("resize")]
    [InlineData("frame")]
    public Task MainWindowStationaryFeedback(string change) => WithOverlay(async (vm, fixtureWindow, _, _) =>
    {
        fixtureWindow.Hide();
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Settle();
        var overlay = window.GetVisualDescendants().OfType<LocalsOverlayControl>().Single();
        var viewer = overlay.GetVisualAncestors().OfType<ZoomPanControl>().Single();
        viewer.AutoFit = false;
        viewer.ZoomLevel = change == "pan" ? 1 : .5;
        Settle();
        var frame = vm.LocalsFrame!.Value;
        var position = overlay.TranslatePoint(Canvas(new(.4, .45), frame, overlay), window)!.Value;
        Assert.Same(overlay, window.InputHitTest(position));
        window.MouseMove(position);
        Assert.Equal("SizeAll", CursorName(overlay));
        var before = window.TranslatePoint(position, overlay);

        switch (change)
        {
            case "zoom":
                viewer.ZoomLevel = 1;
                break;

            case "pan":
                viewer.FindControl<ScrollViewer>("ScrollViewer")!.Offset += new Vector(80, 40);
                break;

            case "resize":
                window.Width += 200;
                break;

            case "frame":
                vm.RotateRightCommand.Execute(null);
                await vm.PendingHistoryCommitTask!.WaitAsync(TestWaits.Condition);
                Assert.NotEqual(frame, vm.LocalsFrame);
                break;
        }

        Settle();
        if (change != "frame") Assert.NotEqual(before, window.TranslatePoint(position, overlay));
        var stationary = CursorName(overlay);
        var action = HoverAction(overlay);
        window.MouseMove(position);
        Assert.Equal(CursorName(overlay), stationary);
        Assert.Equal(HoverAction(overlay), action);
        Assert.NotEqual("SizeAll", stationary);
        window.MouseDown(position, MouseButton.Left);
        AssertPressParity(action, vm);
        vm.DiscardLocalsGesture();
        window.MouseUp(position, MouseButton.Left);
    });

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CapturedExitIntoClippedImage(bool cancel) => WithOverlay(async (vm, window, viewer, overlay) =>
    {
        viewer.ZoomLevel = 1;
        Settle();
        viewer.FindControl<ScrollViewer>("ScrollViewer")!.Offset = new Vector(200, 100);
        Settle();
        var start = overlay.TranslatePoint(Canvas(new(.4, .45), vm.LocalsFrame!.Value, overlay), window)!.Value;
        window.MouseMove(start);
        window.MouseDown(start, MouseButton.Left);
        var cursor = overlay.Cursor;
        var outside = new Point(window.Width - 5, start.Y);
        Assert.True(new Rect(overlay.Bounds.Size).Contains(window.TranslatePoint(outside, overlay)!.Value));
        Assert.NotSame(overlay, window.InputHitTest(outside));
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

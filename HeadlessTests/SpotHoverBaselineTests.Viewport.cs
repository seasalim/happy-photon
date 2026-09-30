using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SpotHoverBaselineTests
{
    [AvaloniaTheory]
    [InlineData("zoom", false)]
    [InlineData("scroll", false)]
    [InlineData("resize", false)]
    [InlineData("zoom", true)]
    [InlineData("scroll", true)]
    [InlineData("resize", true)]
    public Task StationaryViewportFeedback(string change, bool create) => WithViewport((vm, window, viewer, overlay) =>
    {
        if (change == "scroll") viewer.ZoomLevel = 1;

        SettleViewport();
        var normalized = create ? new Point(.2, .25) : new Point(.3125, .375);
        var position = overlay.TranslatePoint(overlay.ToCanvas(normalized), window)!.Value;
        window.MouseMove(position);
        Assert.Equal(create ? "None" : "SizeAll", overlay.Cursor?.ToString());
        var before = window.TranslatePoint(position, overlay)!.Value;

        switch (change)
        {
            case "zoom":
                viewer.ZoomLevel = 1;
                break;

            case "scroll":
                viewer.FindControl<ScrollViewer>("ScrollViewer")!.Offset += new Vector(80, 40);
                break;

            case "resize":
                window.Width += 200;
                break;
        }

        SettleViewport();
        var after = window.TranslatePoint(position, overlay)!.Value;
        Assert.NotEqual(before, after);
        Assert.Same(overlay, window.InputHitTest(position));
        var restingCursor = overlay.Cursor;
        var restingDrawing = Drawing(overlay);
        var restingTarget = Target(overlay);
        output.WriteLine($"R1-1 {change}, create={create}: cursor={restingCursor}; local {before} -> {after}");
        window.MouseMove(position);
        Assert.Same(overlay.Cursor, restingCursor);
        Assert.Equal(Drawing(overlay), restingDrawing);
        Assert.Equal(Target(overlay), restingTarget);

        // A real press independently confirms the resting feedback's action and selection.
        window.MouseDown(position, MouseButton.Left);
        Assert.True(vm.IsSpotsGestureActive);
        var handle = typeof(MainWindowViewModel).GetField("_spotsHandle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(restingTarget.Handle, (SpotHandle)handle.GetValue(vm)!);
        if (restingTarget.Spot != null) Assert.Equal(restingTarget.Spot.Id, vm.SelectedSpot?.Id);

        vm.DiscardSpotsGesture();
        window.MouseUp(position, MouseButton.Left);

        return Task.CompletedTask;
    });

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task ZoomedGestureEndsOutsideViewport(bool cancel, bool outsideWindow) => WithViewport(async (vm, window, viewer, overlay) =>
    {
        viewer.ZoomLevel = 1;
        SettleViewport();
        viewer.FindControl<ScrollViewer>("ScrollViewer")!.Offset = new Vector(200, 100);
        SettleViewport();
        var start = overlay.TranslatePoint(overlay.ToCanvas(new(.3125, .375)), window)!.Value;
        window.MouseMove(start);
        window.MouseDown(start, MouseButton.Left);
        var cursor = overlay.Cursor;
        var outside = new Point(outsideWindow ? -10 : window.Width - 5, start.Y);
        Assert.True(new Rect(overlay.Bounds.Size).Contains(window.TranslatePoint(outside, overlay)!.Value));
        Assert.NotSame(overlay, window.InputHitTest(outside));
        window.MouseMove(outside, RawInputModifiers.LeftMouseButton);
        Assert.Same(cursor, overlay.Cursor);
        Assert.Single(Highlights(overlay));

        if (cancel)
        {
            Assert.True(overlay.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            if (vm.HandleEscapeCommand.ExecutionTask is { } escape) await escape.WaitAsync(TestWaits.Condition);
        }
        else
        {
            window.MouseUp(outside, MouseButton.Left);
            if (vm.PendingHistoryCommitTask is { } commit) await commit.WaitAsync(TestWaits.Condition);
        }

        Assert.False(vm.IsSpotsGestureActive);
        output.WriteLine($"R1-2 cancel={cancel}: cursor={overlay.Cursor?.ToString() ?? "null"}; highlights={Highlights(overlay).Length}");
        Assert.Null(overlay.Cursor);
        Assert.Empty(Highlights(overlay));
        if (cancel) window.MouseUp(outside, MouseButton.Left);
    });

    [AvaloniaFact]
    public Task ViewportChangesPreserveCapturedFeedback() => WithViewport((vm, window, viewer, overlay) =>
    {
        var position = overlay.TranslatePoint(overlay.ToCanvas(new(.3125, .375)), window)!.Value;
        window.MouseMove(position);
        window.MouseDown(position, MouseButton.Left);
        Assert.True(vm.IsSpotsGestureActive);
        var cursor = overlay.Cursor;
        var target = Target(overlay);
        viewer.ZoomLevel = 1;
        SettleViewport();
        viewer.FindControl<ScrollViewer>("ScrollViewer")!.Offset += new Vector(80, 40);
        window.Width += 200;
        SettleViewport();
        Assert.Same(cursor, overlay.Cursor);
        Assert.Equal(target, Target(overlay));
        AssertHighlight(overlay, vm, target.Spot, target.Handle);
        vm.DiscardSpotsGesture();
        var restingCursor = overlay.Cursor;
        var restingDrawing = Drawing(overlay);
        window.MouseMove(position);
        Assert.Same(restingCursor, overlay.Cursor);
        Assert.Equal(restingDrawing, Drawing(overlay));
        window.MouseUp(position, MouseButton.Left);

        return Task.CompletedTask;
    });

    private static (SpotHandle Handle, Repair? Spot) Target(SpotsOverlayControl overlay) =>
        ((SpotHandle, Repair?))typeof(SpotsOverlayControl)
            .GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;

    private static void SettleViewport()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WithViewport(Func<MainWindowViewModel, Window, ZoomPanControl, SpotsOverlayControl, Task> test)
    {
        using var fixture = new CatalogVmFixture("spot-hover-viewport");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(),
            _ => Task.CompletedTask, timeProvider: new TestTimeProvider());
        var image = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
        image.EditSettings.Repairs = FixtureSpots();
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        vm.SelectedSpot = vm.Spots[0];
        var viewer = new ZoomPanControl
        {
            DataContext = vm, Source = vm.PreviewImage, AutoFit = false, ZoomLevel = .5, IsSpotsMode = true,
            OriginalViewPixelSize = new PixelSize(1200, 799)
        };
        var window = new Window { Width = 800, Height = 600, Content = viewer };
        window.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Escape), Command = vm.HandleEscapeCommand });
        using var scope = new TestUiScope(window);
        SettleViewport();

        await test(vm, window, viewer, viewer.FindControl<SpotsOverlayControl>("SpotsOverlay")!);
    }
}

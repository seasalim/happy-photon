using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SpotHoverBaselineTests
{
    public static IEnumerable<object[]> GestureCases()
    {
        foreach (var zone in Grid(1200, 799).Select(p => p.Name))
        {
            foreach (var outside in new[] { false, true })
            {
                foreach (var cancel in new[] { false, true }) yield return [zone, outside, cancel];
            }
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(GestureCases))]
    public async Task GestureFeedbackPersistsAndEnds(string zone, bool outside, bool cancel)
    {
        using var fixture = new CatalogVmFixture("spot-hover-gesture");
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
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var overlay = window.GetVisualDescendants().OfType<SpotsOverlayControl>().Single();
        var map = vm.SpotDisplayMap!;
        var start = overlay.ToCanvas(Grid(map.BaseWidth, map.BaseHeight).Single(p => p.Name == zone).Point);
        Point W(Point p) => overlay.TranslatePoint(p, window)!.Value;
        var capturedExits = 0;
        overlay.PointerExited += (_, e) =>
        {
            if (e.Pointer.Captured == overlay) capturedExits++;
        };
        window.MouseMove(W(start));
        var cursor = overlay.Cursor;
        var outcome = FrozenPressOutcome(zone);
        var action = Enum.Parse<SpotHandle>(outcome.Split("gesture=")[1].Split(';')[0]);
        window.MouseDown(W(start), MouseButton.Left);
        Assert.True(vm.IsSpotsGestureActive);

        if (action == SpotHandle.Create) Assert.Equal("Cross", overlay.Cursor?.ToString());
        else Assert.Same(cursor, overlay.Cursor);

        cursor = overlay.Cursor;
        var spot = action == SpotHandle.Create ? null : vm.SelectedSpot;
        var end = outside ? new Point(-20, -20) : start + new Vector(12, 8);
        window.MouseMove(W(end), RawInputModifiers.LeftMouseButton);
        Assert.Same(cursor, overlay.Cursor);
        if (outside) Assert.True(capturedExits > 0);

        AssertHighlight(overlay, vm, spot, action);
        vm.HideSpotCircles = true;
        Assert.Same(cursor, overlay.Cursor);
        Assert.Empty(Circles(overlay));
        vm.HideSpotCircles = false;
        AssertHighlight(overlay, vm, spot, action);

        if (cancel)
        {
            Assert.True(overlay.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            if (vm.HandleEscapeCommand.ExecutionTask is { } escape) await escape.WaitAsync(TestWaits.Condition);
        }
        else
        {
            window.MouseUp(W(end), MouseButton.Left);
            if (vm.PendingHistoryCommitTask is { } commit) await commit.WaitAsync(TestWaits.Condition);
        }

        Assert.False(vm.IsSpotsGestureActive);

        if (outside)
        {
            Assert.Null(overlay.Cursor);
            Assert.Empty(Highlights(overlay));
        }
        else
        {
            // Compare the no-motion release/cancel result with a fresh hover at that point.
            var restingCursor = overlay.Cursor;
            var restingDrawing = Drawing(overlay);
            window.MouseMove(W(end));
            Assert.Same(restingCursor, overlay.Cursor);
            Assert.Equal(restingDrawing, Drawing(overlay));
            Assert.NotNull(restingCursor);
            Assert.NotEqual("Cross", restingCursor.ToString());
        }

        if (cancel) window.MouseUp(W(end), MouseButton.Left);

        window.MouseMove(W(new Point(-20, -20)));
        Assert.Null(overlay.Cursor);
        Assert.Empty(Highlights(overlay));
        output.WriteLine($"S4 {zone}: outside={outside}; cancel={cancel}; held then reset/re-resolved");
    }
}

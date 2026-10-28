using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using static HappyPhoton.Tests.SpotHoverBaselineTestSupport;

namespace HappyPhoton.Tests;

public sealed partial class SpotHoverBaselineTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task MeasureCurrentBehavior()
    {
        using var fixture = new CatalogVmFixture("spot-hover-baseline");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        GoldenTestPaths.RequireReadableFixture(path);
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(),
            _ => Task.CompletedTask, timeProvider: new TestTimeProvider());
        var image = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        var overlay = new SpotsOverlayControl { DataContext = vm, Width = 600, Height = 400 };
        var window = new Window { Width = 800, Height = 600, Content = overlay };
        using var scope = new TestUiScope(window);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var map = vm.SpotDisplayMap!;
        output.WriteLine($"FIXTURE base={map.BaseWidth}x{map.BaseHeight}; overlay={overlay.Bounds}; drag=(12,8) canvas DIP");
        var spots = FixtureSpots();
        var grid = Grid(map.BaseWidth, map.BaseHeight);
        var handleField = typeof(MainWindowViewModel).GetField("_spotsHandle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Point W(Point p) => overlay.TranslatePoint(p, window)!.Value;
        string CursorName() => overlay.Cursor?.ToString() ?? "null";
        void Reset()
        {
            vm.DiscardSpotsGesture();
            vm.SelectedSpot = null;
            image.EditSettings.Repairs = spots.Select(s => s with { }).ToList();
            vm.SelectedSpot = vm.Spots[0];
            vm.HideSpotCircles = false;
        }

        foreach (var (name, point) in grid)
        {
            Reset();
            var start = overlay.ToCanvas(point);
            var end = start + new Vector(12, 8);
            window.MouseMove(W(start));
            AssertHover(name, overlay, vm, start);
            output.WriteLine($"S2/S3 {name}: cursor={CursorName()}; {Drawing(overlay)}");
            var hoverCursor = overlay.Cursor;
            vm.HideSpotCircles = true;
            Assert.Same(hoverCursor, overlay.Cursor);
            Assert.Empty(Circles(overlay));
            output.WriteLine($"S2/S3 hidden {name}: cursor={CursorName()}; {Drawing(overlay)}");
            vm.HideSpotCircles = false;
            window.MouseDown(W(start), MouseButton.Left);
            Assert.True(vm.IsSpotsGestureActive);
            var kind = handleField.GetValue(vm)!.ToString();
            window.MouseMove(W(end), RawInputModifiers.LeftMouseButton);
            var selection = vm.SelectedSpot!.Id;
            if (!spots.Any(s => s.Id == selection)) selection = "new";
            else selection = selection[..1].ToUpperInvariant();
            var outcome = $"selected={selection}; gesture={kind}; changed={Changes(spots, vm.Spots)}";
            output.WriteLine($"S1 {name}: {outcome}");
            Assert.Equal(FrozenPressOutcome(name), outcome);
            if (kind == "Create") Assert.Equal("Cross", overlay.Cursor?.ToString());
            else Assert.Same(hoverCursor, overlay.Cursor);

            AssertHighlight(overlay, vm, kind == "Create" ? null : vm.SelectedSpot, Enum.Parse<SpotHandle>(kind!));
            output.WriteLine($"S4 drag {name}: cursor={CursorName()}; {Drawing(overlay)}");
            vm.DiscardSpotsGesture();
            window.MouseUp(W(end), MouseButton.Left);
        }

        Reset();
        window.MouseMove(W(overlay.ToCanvas(grid[0].Point)));
        window.MouseMove(W(new Point(-10, -10)));
        Assert.Null(overlay.Cursor);
        Assert.Empty(Highlights(overlay));
        output.WriteLine($"S4 idle-exit: cursor={CursorName()}; {Drawing(overlay)}");
        Reset();
        var cursors = new HashSet<Cursor>(ReferenceEqualityComparer.Instance);
        var byZone = new Dictionary<string, Cursor>();

        for (var i = 0; i < 1000; i++)
        {
            window.MouseMove(W(overlay.ToCanvas(grid[i % grid.Count].Point)));
            Assert.NotNull(overlay.Cursor);
            cursors.Add(overlay.Cursor);
            var zone = grid[i % grid.Count].Name;
            if (byZone.TryGetValue(zone, out var cached)) Assert.Same(cached, overlay.Cursor);
            else byZone.Add(zone, overlay.Cursor);
        }

        Assert.Equal(6, cursors.Count);
        output.WriteLine($"S5 moves=1000; distinct assigned Cursor objects={cursors.Count}");
    }
}

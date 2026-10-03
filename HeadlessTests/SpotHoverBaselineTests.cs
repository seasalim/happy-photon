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

    // Frozen from real press-and-drag observations on 62ab987, without calling HitHandle.
    private static string FrozenPressOutcome(string point) => point switch
    {
        "empty" => "selected=new; gesture=Create; changed=new spot",
        "selected-destination" => "selected=A; gesture=Destination; changed=A.destination",
        "selected-source" => "selected=A; gesture=Source; changed=A.source",
        "B-body" or "B-edge" => "selected=B; gesture=Destination; changed=B.destination",
        "C-body" or "C-edge" or "B-C-overlap" => "selected=C; gesture=Destination; changed=C.destination",
        "D-edge" => "selected=D; gesture=Destination; changed=D.destination",
        _ => "selected=A; gesture=Edge; changed=A.radius"
    };

    private static List<Repair> FixtureSpots() =>
    [
        new() { Id = "a0000000000000000000000000000000", U = .3125, V = .375, Su = .625, Sv = .375, Radius = .03125 },
        new() { Id = "b0000000000000000000000000000000", U = .6875, V = .6875, Su = .5, Sv = .875, Radius = .03125 },
        new() { Id = "c0000000000000000000000000000000", U = .71875, V = .6875, Su = .5, Sv = .75, Radius = .03125 },
        new() { Id = "d0000000000000000000000000000000", U = .3515625, V = .375, Su = .5, Sv = .25, Radius = .015625 }
    ];

    private static List<(string Name, Point Point)> Grid(int width, int height)
    {
        var grid = new List<(string, Point)>
        {
            ("empty", new(.125, .125)),
            ("selected-destination", new(.3125, .375)),
            ("selected-source", new(.625, .375)),
            ("B-body", new(.671875, .6875)),
            ("B-edge", new(.65625, .6875)),
            ("C-body", new(.734375, .6875)),
            ("C-edge", new(.75, .6875)),
            ("D-body-selected-edge-overlap", new(.3515625, .375)),
            ("D-edge", new(.3671875, .375)),
            ("B-C-overlap", new(.703125, .6875))
        };

        for (var degrees = 0; degrees < 360; degrees += 45)
        {
            var angle = degrees * Math.PI / 180;
            grid.Add(($"edge-{degrees}", new(.3125 + .03125 * Math.Cos(angle),
                .375 + .03125 * width / height * Math.Sin(angle))));
        }

        return grid;
    }

    private static string Changes(IReadOnlyList<Repair> before, IReadOnlyList<Repair> after)
    {
        var changes = new List<string>();

        foreach (var spot in after)
        {
            var old = before.FirstOrDefault(s => s.Id == spot.Id);

            if (old == null)
            {
                changes.Add("new spot");
                continue;
            }

            if ((old.U, old.V) != (spot.U, spot.V)) changes.Add($"{spot.Id[..1].ToUpperInvariant()}.destination");
            if ((old.Su, old.Sv) != (spot.Su, spot.Sv)) changes.Add($"{spot.Id[..1].ToUpperInvariant()}.source");
            if (old.Radius != spot.Radius) changes.Add($"{spot.Id[..1].ToUpperInvariant()}.radius");
        }

        return string.Join(",", changes);
    }

    private static IEnumerable<Drawing> Flatten(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
            {
                foreach (var item in Flatten(child)) yield return item;
            }
        }
        else
        {
            yield return drawing;
        }
    }

    private static string Drawing(SpotsOverlayControl overlay)
    {
        var group = new DrawingGroup();
        using (var context = group.Open()) overlay.Render(context);
        var circles = Flatten(group).OfType<GeometryDrawing>()
            .Where(d => d.Pen != null && d.Geometry != null).ToArray();
        var values = circles.Select(d => $"{d.Geometry!.GetType().Name}:{d.Geometry.Bounds.Width:F2}x{d.Geometry.Bounds.Height:F2}@{d.Geometry.Bounds.Center.X:F2},{d.Geometry.Bounds.Center.Y:F2}/{d.Pen!.Thickness}/{(d.Pen.DashStyle == null ? "solid" : "dash")}");

        return $"geometry-strokes={circles.Length} [{string.Join(";", values)}]";
    }
}


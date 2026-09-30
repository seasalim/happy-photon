using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
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

public sealed partial class LocalsHoverBaselineTests(ITestOutputHelper output)
{
    private static List<LocalAdjustment> FixtureLocals() =>
    [
        new() { Id = "a0000000000000000000000000000000", Type = "radial", Cu = .4, Cv = .45, Angle = 30, Rx = .18, Ry = .09, Feather = .4 },
        new() { Id = "b0000000000000000000000000000000", Type = "linear", Ordinal = 2, Cu = .76, Cv = .7, Angle = 120, Feather = .15 },
        new() { Id = "c0000000000000000000000000000000", Type = "brush", Ordinal = 3, Strokes = [new() { Points = [new(3277, 13107)] }] }
    ];

    private static async Task WithOverlay(Func<MainWindowViewModel, Window, ZoomPanControl, LocalsOverlayControl, Task> test)
    {
        using var fixture = new CatalogVmFixture("locals-hover-baseline");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(),
            _ => Task.CompletedTask, timeProvider: new TestTimeProvider());
        var image = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
        image.EditSettings.Locals = FixtureLocals();
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedLocal = vm.Locals[0];
        var viewer = new ZoomPanControl
        {
            DataContext = vm, Source = vm.PreviewImage, AutoFit = false, ZoomLevel = .5,
            IsLocalsMode = true, OriginalViewPixelSize = new PixelSize(1200, 799)
        };
        viewer.Bind(ZoomPanControl.SourceProperty, new Binding(nameof(MainWindowViewModel.PreviewImage)));
        var window = new Window { Width = 800, Height = 600, Content = viewer };
        window.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Escape), Command = vm.HandleEscapeCommand });
        using var scope = new TestUiScope(window);
        Settle();
        var overlay = viewer.FindControl<LocalsOverlayControl>("LocalsOverlay")!;
        Assert.True(overlay.IsEffectivelyVisible);

        await test(vm, window, viewer, overlay);
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    // Independent analytic coordinates, without HitHandle, HitPin, or a press resolver.
    private static List<(string Name, Point Point)> Grid(LocalAdjustment local, LocalsFrame frame)
    {
        Point Offset(double x, double y)
        {
            var a = local.Angle * Math.PI / 180;

            return new(local.Cu + (x * Math.Cos(a) - y * Math.Sin(a)) * frame.LongEdge / frame.Width,
                local.Cv + (x * Math.Sin(a) + y * Math.Cos(a)) * frame.LongEdge / frame.Height);
        }

        var points = new List<(string, Point)>
        {
            ("empty", new(.1, .1)), ("center", new(local.Cu, local.Cv)),
            ("radial-pin", new(.4, .45)), ("linear-pin", new(.76, .7)),
            ("brush-pin", new(3277d / 16384, 13107d / 16384))
        };

        if (local.IsRadial)
        {
            points.AddRange([("x+", Offset(local.Rx, 0)), ("x-", Offset(-local.Rx, 0)),
                ("y+", Offset(0, local.Ry)), ("y-", Offset(0, -local.Ry)),
                ("rotation", Offset(local.Rx + .08, 0))]);

            foreach (var degrees in new[] { 45, 135, 225, 315 })
            {
                var a = degrees * Math.PI / 180;
                points.Add(($"ring-{degrees}", Offset(local.Rx * .6 * Math.Cos(a), local.Ry * .6 * Math.Sin(a))));
            }
        }
        else
        {
            points.AddRange([("feather+", Offset(local.Feather / 2, 0)),
                ("feather-", Offset(-local.Feather / 2, 0)), ("direction", Offset(local.Feather / 2 + .08, 0))]);
        }

        return points;
    }

    private static Point Canvas(Point p, LocalsFrame frame, LocalsOverlayControl overlay) =>
        new((p.X - frame.CropX) / frame.CropWidth * overlay.Bounds.Width,
            (p.Y - frame.CropY) / frame.CropHeight * overlay.Bounds.Height);

    private static string CursorName(LocalsOverlayControl overlay) => overlay.Cursor?.ToString() ?? "null";

    private static string Gesture(MainWindowViewModel vm) => vm.IsLocalsGestureActive
        ? vm.IsBrushStrokeActive ? "BrushStroke" : typeof(MainWindowViewModel)
            .GetField("_localsHandle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!.ToString()!
        : "none";

    private static string Changes(IReadOnlyList<LocalAdjustment> before, IReadOnlyList<LocalAdjustment> after)
    {
        var changes = new List<string>();

        foreach (var local in after)
        {
            var old = before.FirstOrDefault(p => p.Id == local.Id);

            if (old == null)
            {
                changes.Add("new-" + local.Type);
                continue;
            }

            if ((old.Cu, old.Cv) != (local.Cu, local.Cv)) changes.Add(local.Type + ".center");
            if (old.Angle != local.Angle) changes.Add(local.Type + ".angle");
            if (old.Rx != local.Rx) changes.Add(local.Type + ".Rx");
            if (old.Ry != local.Ry) changes.Add(local.Type + ".Ry");
            if (old.Feather != local.Feather) changes.Add(local.Type + ".feather");
            if (old.Strokes != local.Strokes) changes.Add(local.Type + ".strokes");
        }

        return changes.Count == 0 ? "none" : string.Join(",", changes);
    }

    private static void Reset(MainWindowViewModel vm, string selected)
    {
        vm.DiscardLocalsGesture();
        vm.SelectedImage!.EditSettings.Locals = FixtureLocals();
        vm.SelectedLocal = vm.Locals.Single(l => l.Type == selected);
    }

    [AvaloniaFact]
    public Task PressAndHoverGrid() => WithOverlay((vm, window, viewer, overlay) =>
    {
        var frame = vm.LocalsFrame!.Value;
        output.WriteLine($"FIXTURE frame={frame}; overlay={overlay.Bounds}; drag=(12,8) DIP");
        Point W(Point p) => overlay.TranslatePoint(p, window)!.Value;

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

                var before = vm.Locals.Select(l => l with { }).ToArray();
                var start = Canvas(point, frame, overlay);
                var end = start + new Vector(12, 8);
                Assert.Same(overlay, window.InputHitTest(W(start)));
                window.MouseMove(W(start));
                output.WriteLine($"L2 {selected}/{name}: {CursorName(overlay)}; normalized={point}");
                Assert.Equal(ExpectedCursor(selected, name), CursorName(overlay));
                var hover = HoverAction(overlay);
                window.MouseDown(W(start), MouseButton.Left);
                AssertPressParity(hover, vm);
                var gesture = Gesture(vm);
                window.MouseMove(W(end), RawInputModifiers.LeftMouseButton);
                var outcome = $"selected={vm.SelectedLocal?.Type}; gesture={gesture}; changed={Changes(before, vm.Locals)}";
                output.WriteLine($"L1 {selected}/{name}: {outcome}");
                Assert.Equal(FrozenPressOutcome(selected, name), outcome);
                vm.DiscardLocalsGesture();
                window.MouseUp(W(end), MouseButton.Left);
            }
        }

        foreach (var mode in new[] { "armed-radial", "armed-linear", "brush", "hue" })
        {
            Reset(vm, mode == "brush" ? "brush" : "radial");
            if (mode == "armed-radial") vm.AddRadialCommand.Execute(null);
            if (mode == "armed-linear") vm.AddLinearCommand.Execute(null);
            if (mode == "hue") vm.ToggleLocalHuePickCommand.Execute(null);

            foreach (var point in new[] { new Point(.1, .1), new Point(.4, .45), new Point(.76, .7), new Point(3277d / 16384, 13107d / 16384) })
            {
                window.MouseMove(W(Canvas(point, frame, overlay)));
                output.WriteLine($"L2 {mode}@{point}: {CursorName(overlay)}");
                Assert.Equal(mode == "brush" ? "None" : "Cross", CursorName(overlay));
            }

            vm.DiscardLocalsGesture();
        }

        return Task.CompletedTask;
    });

    [AvaloniaFact]
    public Task CursorInstances() => WithOverlay((vm, window, viewer, overlay) =>
    {
        foreach (var mode in new[] { "shape", "armed-radial", "armed-linear", "brush", "hue" })
        {
            Reset(vm, mode == "brush" ? "brush" : "radial");
            if (mode == "armed-radial") vm.AddRadialCommand.Execute(null);
            if (mode == "armed-linear") vm.AddLinearCommand.Execute(null);
            if (mode == "hue") vm.ToggleLocalHuePickCommand.Execute(null);
            var points = Grid(vm.Locals[0], vm.LocalsFrame!.Value)
                .Select(p => overlay.TranslatePoint(Canvas(p.Point, vm.LocalsFrame.Value, overlay), window)!.Value).ToArray();
            var seen = new HashSet<Cursor>(ReferenceEqualityComparer.Instance);

            foreach (var point in points)
            {
                window.MouseMove(point);
                seen.Add(overlay.Cursor!);
            }

            var initial = seen.Count;

            for (var i = 0; i < 1000; i++)
            {
                window.MouseMove(points[i % points.Length]);
                seen.Add(overlay.Cursor!);
            }

            output.WriteLine($"L5 {mode}: new-assigned-Cursor-identities={seen.Count - initial}/1000 moves");
            Assert.Equal(initial, seen.Count);
        }

        vm.DiscardLocalsGesture();

        return Task.CompletedTask;
    });
}




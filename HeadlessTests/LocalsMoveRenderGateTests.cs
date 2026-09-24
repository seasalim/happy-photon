using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// G3: identical synchronous move plus actual overlay-render boundary for all workloads.
public sealed class LocalsMoveRenderGateTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task CrowdedGradientMoveAndRenderReference()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Opt-in G3 move plus overlay render reference");
#if DEBUG
        Assert.Skip("G3 requires a Release build");
#endif
        using var fixture = new CatalogVmFixture("locals-move-render-g3");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog,
            new LocalTestLoader(width: 1200, height: 800), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        var image = new ImageFile(fixture.Path("synthetic.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);

        image.EditSettings.Locals = CrowdedLocals();
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedLocal = vm.Locals[0];
        vm.ShowLocalMask = true;
        Assert.True(vm.CanEditLocals);
        Assert.NotNull(vm.LocalsFrame);
        Assert.Equal(7, vm.Locals.Count(local => !local.IsBrush));
        var brush = Assert.Single(vm.Locals, local => local.IsBrush);
        Assert.Equal(95, brush.Strokes!.Count);
        Assert.Equal(3900, brush.Strokes.Sum(stroke => stroke.Points.Count));
        Assert.Equal("linear", vm.SelectedLocal!.Type);

        var size = vm.PreviewImage!.PixelSize;
        var overlay = new LocalsOverlayControl { DataContext = vm };
        overlay.Measure(new Size(size.Width, size.Height));
        overlay.Arrange(new Rect(0, 0, size.Width, size.Height));
        using var target = new RenderTargetBitmap(size, new Vector(96, 96));
        try
        {
            Assert.True(overlay.IsVisible);
            var origin = new Point(vm.SelectedLocal.Cu, vm.SelectedLocal.Cv);
            Assert.True(vm.BeginLocalsGesture(LocalHandle.Center, origin));
            var times = new double[100];
            for (var i = 0; i < 120; i++)
            {
                var point = origin + new Vector((i + 1) * .001, (i + 1) * .0005);
                var distance = Math.Sqrt(Math.Pow((point.X - origin.X) * size.Width, 2) +
                    Math.Pow((point.Y - origin.Y) * size.Height, 2));
                var before = vm.SelectedLocal.Cu;
                var start = Stopwatch.GetTimestamp();
                vm.MoveLocalsGesture(point, distance);
                // A real Skia DrawingContext, including its flush/disposal, for every frame.
                using (var context = target.CreateDrawingContext()) overlay.Render(context);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Assert.NotEqual(before, vm.SelectedLocal.Cu);
                Assert.Equal(point.X, vm.SelectedLocal.Cu, 10);
                if (i >= 20) times[i - 20] = elapsed;
            }
            Assert.True(vm.IsLocalsGestureActive);
            await vm.CompleteLocalsGestureAsync();
            Assert.False(vm.IsLocalsGestureActive);
            Assert.Equal(3900, vm.Locals.Single(local => local.IsBrush).Strokes!
                .Sum(stroke => stroke.Points.Count));
            var sorted = times.Order().ToArray();
            var median = (sorted[49] + sorted[50]) / 2;
            var p95 = sorted[94]; // Nearest-rank percentile over the 100 measured moves.
            output.WriteLine($"G3_reference pid={Environment.ProcessId} cpu={Environment.ProcessorCount} " +
                $"surface={size.Width}x{size.Height}@96dpi mask=on gradients=7 strokes=95 points=3900 " +
                $"warmup=20 moves=100 median_ms={median:F4} p95_ms={p95:F4} " +
                $"min_ms={sorted[0]:F4} max_ms={sorted[^1]:F4}");
            output.WriteLine($"move_ms=[{string.Join(',', times.Select(t => t.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)))}]");
            Assert.InRange(median, 6.29 * .75, 6.29 * 1.25);
            // Erase draws a different in-progress feedback, so both modes are gated.
            foreach (var mode in new[] { "paint", "erase" })
            {
                await MeasureBrush(false, mode, median, p95);
                await MeasureBrush(true, mode, median, p95);
            }

            async Task MeasureBrush(bool longStroke, string mode, double controlMedian, double controlP95)
            {
                image.EditSettings.Locals = CrowdedLocals();
                var local = image.EditSettings.Locals[^1];
                if (longStroke) local.Strokes = [];
                vm.SelectedLocal = local;
                vm.BrushSize = 1 + 99 * Math.Log(30) / Math.Log(250);
                vm.BrushMode = mode;
                await vm.PendingLocalMaskTask;
                var initial = longStroke ? 0 : 3900;
                var count = longStroke ? 3990 : 100;
                Point Position(int i) => new(.1 + (i % 100) * .008, .1 + (i / 100) * .02);
                var measured = new List<double>();
                var firstStarted = Stopwatch.GetTimestamp();
                Assert.True(vm.BeginBrushStroke(Position(0)));
                using (var context = target.CreateDrawingContext()) overlay.Render(context);
                if (!longStroke) measured.Add(Stopwatch.GetElapsedTime(firstStarted).TotalMilliseconds);
                for (var i = 1; i < count; i++)
                {
                    var started = Stopwatch.GetTimestamp();
                    var kept = vm.ExtendBrushStroke(Position(i), size.Width);
                    using (var context = target.CreateDrawingContext()) overlay.Render(context);
                    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    Assert.True(kept, $"Rejected G3 point {i}");
                    if (!longStroke || i >= count - 100) measured.Add(elapsed);
                }
                Assert.Equal(count, vm.LiveBrushStroke!.Points.Count);
                Assert.Equal(initial + count, vm.Locals.Sum(l => l.Strokes?.Sum(s => s.Points.Count) ?? 0));
                var sortedBrush = measured.Order().ToArray();
                var brushMedian = sortedBrush[sortedBrush.Length / 2];
                var brushP95 = sortedBrush[(int)Math.Ceiling(sortedBrush.Length * .95) - 1];
                Assert.Equal(mode, vm.LiveBrushStroke.Mode);
                output.WriteLine($"G3_brush workload={(longStroke ? "long" : "crowded")} mode={mode} moves={measured.Count} " +
                    $"median_ms={brushMedian:F4} p95_ms={brushP95:F4} control_median_ms={controlMedian:F4} control_p95_ms={controlP95:F4}");
                Assert.True(brushMedian <= 1.25 * controlMedian, "Brush median exceeds 1.25x control");
                Assert.True(brushP95 <= 1.25 * controlP95, "Brush p95 exceeds 1.25x control");
                vm.DiscardLocalsGesture();
            }
        }
        finally { overlay.DataContext = null; }
    }

    private static List<LocalAdjustment> CrowdedLocals()
    {
        var locals = Enumerable.Range(0, 7).Select(i => new LocalAdjustment
        {
            Type = i % 2 == 0 ? "linear" : "radial", Ordinal = i + 1,
            Cu = .2 + i * .08, Cv = .3 + i * .05, Angle = i * 25,
            Rx = .2, Ry = .15, Feather = .4
        }).ToList();
        locals.Add(new LocalAdjustment
        {
            Type = "brush", Ordinal = 8,
            Strokes = [.. Enumerable.Range(0, 95).Select(s => new LocalBrushStroke
            {
                Mode = s % 5 == 4 ? "erase" : "paint", Radius = .03, Feather = .5, Flow = 1,
                // 5 * 42 + 90 * 41 = 3,900 quantized points.
                Points = [.. Enumerable.Range(0, s < 5 ? 42 : 41).Select(p =>
                    new LocalBrushPoint(1024 + p * 340, 1024 + s * 145))]
            })]
        });
        return locals;
    }
}


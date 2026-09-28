using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SpotsMoveRenderGateTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task G1S64DestinationAndSourceMoveRender()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in WP4 G1");
        Assert.True(double.TryParse(Environment.GetEnvironmentVariable("HAPPY_PHOTON_SPOTS_G1_CONTROL"),
            System.Globalization.CultureInfo.InvariantCulture, out var control), "Supply the same-session immutable 3e569fe control");
        Assert.InRange(control, 4.5, 9);
        using var fixture = new CatalogVmFixture("spots-move-render");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new LocalTestLoader(width: 1200, height: 800),
            _ => Task.CompletedTask, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        var image = new ImageFile(fixture.Path("synthetic.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        image.EditSettings.Repairs = HealWorkloads.Repairs(HealWorkloads.S64());
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        vm.SelectedSpot = vm.Spots[0];
        var overlay = new SpotsOverlayControl { DataContext = vm, Width = 1200, Height = 800 };
        using var scope = new TestUiScope(new Window { Width = 1200, Height = 800, Content = overlay });
        scope.Window!.MouseMove(new(600, 400));
        Assert.Equal("None", overlay.Cursor?.ToString());
        using var target = new RenderTargetBitmap(new PixelSize(1200, 800), new Vector(96, 96));
        foreach (var handle in new[] { SpotHandle.Destination, SpotHandle.Source })
        {
            var spot = vm.SelectedSpot!;
            var origin = handle == SpotHandle.Source ? new Point(spot.Su, spot.Sv) : new Point(spot.U, spot.V);
            Assert.True(vm.BeginSpotsGesture(handle, origin));
            var times = new List<double>();
            for (var i = 0; i < 120; i++)
            {
                var point = origin + new Vector((i + 1) * .001, (i + 1) * .0005);
                var before = spot with { };
                var started = Stopwatch.GetTimestamp();
                vm.MoveSpotsGesture(point, 100);
                using (var context = target.CreateDrawingContext()) overlay.Render(context);
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Assert.NotEqual(before, vm.SelectedSpot);
                if (i >= 20) times.Add(elapsed);
            }
            var median = times.Order().ElementAt(50);
            output.WriteLine($"G1 {handle} S64 1200x800 moves=100 median={median:F4} ms control={control:F4} threshold={control * 1.25:F4}");
            Assert.True(median <= 1.25 * control);
            vm.DiscardSpotsGesture();
        }
    }
}

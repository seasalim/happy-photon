using System.Diagnostics;
using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalRangeOverlayGateTests
{
    [AvaloniaFact]
    public async Task QualifiedRangeHuePick()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in picker gate");
        var standard = Environment.GetEnvironmentVariable("HAPPY_PHOTON_LOCALS_FIXTURE") == "standard";
        var path = GoldenTestPaths.Asset(standard ? "iphone-14-pro-iso-1000.heic" : "pentax-k-r.dng");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        using var fixture = new CatalogVmFixture("hue-pick-gate");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), _ => Task.CompletedTask);
        var image = new ImageFile(path);
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        vm.Browse.SetImages([image]); vm.IsDevelopMode = true; vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        // The neutral Canon high-ISO fixture has no accepted point on a 199x199 scan.
        // Keep it for all renderer gates; use this pinned saturated Pentax point for accepted RAW sampling.
        var point = standard ? new Point(.5, .5) : new Point(.35, .345);
        var times = new double[15];
        for (var i = 0; i < times.Length; i++)
        {
            var clock = Stopwatch.StartNew();
            using var lease = vm.ImageService.Previews.AcquireLocalRangeBase(image, image.EditSettings, 1600, vm.PreviewImage);
            Assert.NotNull(lease);
            var result = LocalRangeSampling.Pick(lease.Base, image.EditSettings, point, vm.PreviewImage!.PixelSize);
            times[i] = clock.Elapsed.TotalMilliseconds;
            output.WriteLine($"sample={i} ms={times[i]:F4} point={point} hue={result.Hue} rejection={result.Rejection} count={result.Count}");
            Assert.NotNull(result.Hue);
        }
        output.WriteLine($"pick fixture={(standard ? "HEIC" : "Pentax-RAW")} median_ms={times.Order().ElementAt(7):F4} all_accepted=True");
        Assert.True(times.Order().ElementAt(7) <= 60);
        // Scan after timing so the substitution evidence cannot warm the accepted-pick gate.
        if (!standard) await ScanCanonAsync();
    }

    private async Task ScanCanonAsync()
    {
        var path = GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2");
        Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
        using var fixture = new CatalogVmFixture("canon-pick-scan");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, new RawBaseLoader(), _ => Task.CompletedTask);
        var image = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
        vm.Browse.SetImages([image]); vm.IsDevelopMode = true; vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        using var lease = vm.ImageService.Previews.AcquireLocalRangeBase(image, image.EditSettings, 1600, vm.PreviewImage);
        Assert.NotNull(lease);
        var reasons = new SortedDictionary<string, int>();
        double bestReliability = -1, coherenceAtBestReliability = 0, bestCoherence = -1;
        var bestPoint = default(Point);
        for (var y = 1; y < 200; y++)
        for (var x = 1; x < 200; x++)
        {
            var point = new Point(x / 200d, y / 200d);
            var result = LocalRangeSampling.Pick(lease.Base, image.EditSettings, point, vm.PreviewImage!.PixelSize, out var reliability, out var coherence);
            var reason = result.Rejection ?? "accepted";
            reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            if (reliability > bestReliability)
            { bestReliability = reliability; coherenceAtBestReliability = coherence; bestPoint = point; }
            bestCoherence = Math.Max(bestCoherence, coherence);
        }
        output.WriteLine($"Canon scan grid=199x199 points={reasons.Values.Sum()} base={lease.Base.Pixels.Width}x{lease.Base.Pixels.Height} " +
            $"reasons=[{string.Join(", ", reasons.Select(pair => $"{pair.Key}={pair.Value}"))}] " +
            $"best_reliability={bestReliability:F9} coherence_at_best={coherenceAtBestReliability:F9} " +
            $"best_point={bestPoint} best_coherence={bestCoherence:F9}");
        Assert.Equal(39601, reasons.Values.Sum());
        Assert.Equal(0, reasons.GetValueOrDefault("accepted"));
    }
}

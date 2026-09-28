using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class AutomaticRepairSourceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1600, 1067)]
    [InlineData(1600, 120)]
    [InlineData(120, 1600)]
    public void RankedSourcesAreDeterministicInsideAndNonoverlapping(int width, int height)
    {
        using var pixels = new MagickImage("gradient:#112233-#ccbbaa", (uint)width, (uint)height);
        using var basis = new BaseImage(pixels, new BaseImageInfo(BaseSourceKind.Standard, false,
            BaseDecodeSettings.Default, null, null, 6504, 0, false, null, 1, width, height));
        var spots = RepairTestWorkload.S64();
        spots.AddRange(new[] { (0d, 0d), (1d, 1d), (0d, 1d), (1d, 0d), (.5, .5) }
            .Select(p => new Repair { U = p.Item1, V = p.Item2, Radius = .1 }));
        foreach (var spot in spots)
        {
            var candidates = AutomaticRepairSource.Rank(basis, spot);
            Assert.NotEmpty(candidates);
            Assert.Equal(candidates, AutomaticRepairSource.Rank(basis, spot));
            Assert.Equal(candidates.OrderBy(candidate => candidate.Score), candidates);
            foreach (var source in candidates)
            {
                var placed = spot with { Su = source.U, Sv = source.V };
                Assert.Equal((source.U, source.V), RepairGeometry.ClampSource(placed, width, height));
                var distance = Math.Sqrt(Math.Pow((spot.U - source.U) * width, 2) + Math.Pow((spot.V - source.V) * height, 2));
                Assert.True(distance >= 2 * RepairGeometry.EffectiveRadius(spot.Radius, width, height));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void G2AutomaticSourceS64AgainstLh8(bool standard)
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1", "Opt-in WP4 G2");
        PerfEnvironment.AssertFullCpu();
        var path = GoldenTestPaths.Asset(standard ? "iphone-14-pro-iso-1000.heic" : "canon-eos-6d-iso-6400.cr2");
        Assert.Equal(SourceAvailability.AvailableLocally, new SourceAvailabilityService().GetAvailability(path));
        var loader = new GatedBaseImageLoader(new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), new SourceAvailabilityService());
        using var pair = loader.LoadPreviewBaseWithOutcome(new ImageFile(path), BaseDecodeSettings.Default, CancellationToken.None).Pair!;
        var pipeline = new RenderPipeline();
        var settings = HealWorkloads.LH8();
        void Tick() { using var result = pipeline.Render(new(pair.Interactive, settings, RenderIntent.Preview, 1600, new(false, false))); }
        for (var i = 0; i < 10; i++) Tick();
        var control = Enumerable.Range(0, 5).Select(_ => Measure(Tick)).Order().ElementAt(2);
        var spots = RepairTestWorkload.S64();
        foreach (var spot in spots) AutomaticRepairSource.Rank(pair.Interactive, spot);
        var times = spots.Select(spot => Measure(() => Assert.NotEmpty(AutomaticRepairSource.Rank(pair.Interactive, spot)))).Order().ToArray();
        output.WriteLine($"G2 {(standard ? "HEIC" : "RAW")} LH8={control:F4} ms automatic median={times[32]:F4} max={times[^1]:F4} ms");
        Assert.InRange(control, 38, 60);
        Assert.True(times[32] <= 30);
        Assert.True(times[^1] <= 60);
    }

    private static double Measure(Action action)
    {
        var started = Stopwatch.GetTimestamp();
        action();
        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }
}

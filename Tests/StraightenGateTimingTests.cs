using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class StraightenGateTimingTests(ITestOutputHelper output)
{
    [Fact]
    public void G5Before() => Measure(detector: false);

    [Fact]
    public void G5Detector() => Measure(detector: true);

    private void Measure(bool detector)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") == "1", "Opt-in straighten timing");
        Assert.Equal("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU"));
        PerfEnvironment.AssertFullCpu();
#if DEBUG
        Assert.Fail("Use Release");
#endif
        var name = Environment.GetEnvironmentVariable("HAPPY_PHOTON_STRAIGHTEN_FIXTURE") ??
            "canon-eos-6d-iso-6400.cr2";
        Assert.Contains(name, new[] { "canon-eos-6d-iso-6400.cr2", "iphone-14-pro-iso-1000.heic", "nikon-d300-colorchecker.nef",
            "skyline", "fbm" });
        // WP3's fallback frames are synthetic 1600x1067 scenes, timed through the detector only.
        var synthetic = name is "skyline" or "fbm";
        Assert.True(detector || !synthetic, "Synthetic G5 frames time the detector only");
        using var pair = synthetic ? null : StraightenGateFixtures.Load(name);
        using var frame = !synthetic ? null :
            name == "skyline" ? StraightenGateScenes.Create(name, false) : StraightenGateNegativeScenes.Create(name, false);
        var pixels = frame ?? pair!.Interactive.Pixels;
        var settings = new EditSettings();
        var expected = name == "skyline" ? HorizonDetection.Tier.Skyline :
            name == "nikon-d300-colorchecker.nef" ? HorizonDetection.Tier.Lines : HorizonDetection.Tier.Orientation;
        var tier = HorizonDetection.Tier.Orientation;

        void Tick()
        {
            if (detector)
            {
                _ = HorizonDetection.Detect(pixels, out var diagnostics);
                tier = diagnostics.Tier;

                return;
            }

            using var result = new RenderPipeline().Render(
                new(pair!.Interactive, settings, RenderIntent.Preview, 1600, new(false, false)));
        }

        for (var warmup = 0; warmup < 3; warmup++)
        {
            Tick();
            if (detector) Assert.Equal(expected, tier);
        }

        var samples = new double[11];

        for (var sample = 0; sample < samples.Length; sample++)
        {
            var start = Stopwatch.GetTimestamp();
            Tick();
            samples[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (detector) Assert.Equal(expected, tier);
        }

        var median = FinishingGateSupport.Median(samples);
        output.WriteLine("STRAIGHTEN " + JsonSerializer.Serialize(new { gate = detector ? "G5Detector" : "G5",
            pid = Environment.ProcessId,
            values = new { fixture = name, cpu = Environment.ProcessorCount, width = pixels.Width,
                height = pixels.Height, warmups = 3, samples, median, tier = detector ? tier.ToString() : null } }));
        // Owner ruling, run 316: line-heavy bases (the D300) may take 55 ms.
        var limit = synthetic || name == "nikon-d300-colorchecker.nef" ? 55 : 50;
        if (detector) Assert.True(median <= limit, $"Detector exceeded {limit} ms: {median}");
    }
}

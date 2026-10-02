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
        Assert.Contains(name, new[] { "canon-eos-6d-iso-6400.cr2", "iphone-14-pro-iso-1000.heic", "nikon-d300-colorchecker.nef" });
        using var pair = StraightenGateFixtures.Load(name);
        var basis = pair.Interactive;
        var settings = new EditSettings();
        void Tick()
        {
            if (detector)
            {
                _ = HorizonDetection.Detect(basis.Pixels);

                return;
            }

            using var result = new RenderPipeline().Render(
                new(basis, settings, RenderIntent.Preview, 1600, new(false, false)));
        }

        for (var warmup = 0; warmup < 3; warmup++)
        {
            Tick();
        }

        var samples = new double[11];

        for (var sample = 0; sample < samples.Length; sample++)
        {
            var start = Stopwatch.GetTimestamp();
            Tick();
            samples[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        var median = FinishingGateSupport.Median(samples);
        output.WriteLine("STRAIGHTEN " + JsonSerializer.Serialize(new { gate = detector ? "G5Detector" : "G5",
            pid = Environment.ProcessId,
            values = new { fixture = name, cpu = Environment.ProcessorCount, width = basis.Pixels.Width,
                height = basis.Pixels.Height, warmups = 3, samples, median } }));
        if (detector) Assert.True(median <= 50, $"Detector exceeded 50 ms: {median}");
    }
}

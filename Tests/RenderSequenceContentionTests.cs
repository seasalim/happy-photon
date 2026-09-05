using System.Diagnostics;
using System.Reflection;
using HappyPhoton.Services;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

// Run alone via MeasureRenderSequence.ps1; no file decode and no cancellation.
// Baseline at 73d631c (HEAD 5aec65c, production unchanged), 2026-09-05, CPU=24.
// Three fresh Release processes, FULL_CPU=1; ten untimed warm-ups, then
// median-of-10 alone and median-of-10 concurrent. Resting: 3200x2133, cap 2;
// interactive: linear resize of that same synthetic RAW base to long edge 1600.
// All ten starts observed resting in flight after raw-crossing; all renders succeeded.
// Process: alone ms, concurrent ms, concurrent/alone:
// 1: 91.997500, 106.474150, 1.157359167
// 2: 97.968200, 101.315250, 1.034164658
// 3: 93.053100, 101.041200, 1.085844534
// Concurrent median across processes = 101.315250 ms.
// Largest spread = 5.432950 ms; floor_G4 = max/min - 1 = 0.053769650.
// Fractional spread is used because the planned after/before bound is a ratio.
// The single-warm-up probe was diagnostic only: alone samples fell from ~270 to ~90 ms.
// Observed baselines, not yet user-approved acceptance gates.
public sealed class RenderSequenceContentionTests(ITestOutputHelper output)
{
    [Fact]
    public async Task MeasureInteractiveLatencyDuringResting()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Opt in with HAPPY_PHOTON_PERF=1 and HAPPY_PHOTON_FULL_CPU=1.");
#if DEBUG
        Assert.Skip("Contention measurements require Release.");
#endif
        Assert.Equal("1", Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU"));
        var samples = int.Parse(Environment.GetEnvironmentVariable("HAPPY_PHOTON_CONTENTION_SAMPLES") ?? "10");
        var edge = int.Parse(Environment.GetEnvironmentVariable("HAPPY_PHOTON_RESTING_EDGE") ?? "3200");
        Assert.InRange(samples, 1, 10);
        Assert.True(edge >= 3200);
        // Reuse the exact golden fixture builder without editing G1/G2 or their goldens.
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        using var large = (BaseImage)typeof(RenderSequenceGoldenTests)
            .GetMethod("CreateBase", flags)!.Invoke(null, ["raw", edge, edge * 2 / 3])!;
        var settings = (EditSettings)typeof(RenderSequenceGoldenTests)
            .GetMethod("Settings", flags)!.Invoke(null, null)!;
        var smallPixels = (ImageMagick.MagickImage)large.Pixels.Clone();
        smallPixels.Resize(1600, 1067);
        using var small = new BaseImage(smallPixels, large.Info);
        Assert.Equal(1600u, small.Pixels.Width);
        Assert.Equal((uint)edge, large.Pixels.Width);
        var interactive = new RenderRequest(small, settings, RenderIntent.Preview,
            1600, new RenderOptions(false, false));
        var restingRequest = new RenderRequest(large, settings, RenderIntent.Preview,
            edge, new RenderOptions(false, false));
        var pipeline = new RenderPipeline();
        // Fixed untimed warm-up: the initial probe showed startup effects in alone samples.
        for (var warm = 0; warm < 10; warm++)
            using (pipeline.Render(interactive)) { }
        var alone = Enumerable.Range(0, 10).Select(_ => Measure()).ToArray();
        using var stageStarted = new ManualResetEventSlim();
        var execution = RenderExecutionOptions.Resting(CancellationToken.None, 2,
            stage => { if (stage == "raw-crossing") stageStarted.Set(); });
        var resting = Task.Run(() => pipeline.RenderResting(restingRequest, execution));
        var concurrent = new List<double>();
        try
        {
            Assert.True(stageStarted.Wait(TestWaits.Condition), "Resting never entered raw-crossing.");
            for (var sample = 0; sample < samples; sample++)
            {
                if (resting.IsCompleted)
                    output.WriteLine($"EARLY resting completed before sample {sample + 1}; collected={concurrent.Count}");
                Assert.False(resting.IsCompleted,
                    "Resting completed early: enlarge HAPPY_PHOTON_RESTING_EDGE or reduce HAPPY_PHOTON_CONTENTION_SAMPLES.");
                concurrent.Add(Measure());
            }
        }
        finally
        {
            using var completed = await resting;
            output.WriteLine($"RESTING completed successfully; edge={edge}; cap=2; " +
                $"expectedWorkers={execution.CapWorkers(Environment.ProcessorCount)} (formula; the cap is observed through the concurrent latency); cancellation=False");
            output.WriteLine("ALONE samples ms=" + string.Join(",", alone.Select(t => t.ToString("F6"))));
            output.WriteLine("CONCURRENT samples ms=" + string.Join(",", concurrent.Select(t => t.ToString("F6"))));
        }
        var aloneMs = Median(alone);
        var concurrentMs = Median(concurrent.ToArray());
        output.WriteLine($"CONTENTION alone={aloneMs:F6} concurrent={concurrentMs:F6} ms " +
            $"ratio={concurrentMs / aloneMs:F9} warmups=10 aloneSamples=10 concurrentSamples={samples} " +
            $"budget150ms={(concurrentMs <= 150 ? "PASS" : "FAIL")}");

        double Measure()
        {
            var start = Stopwatch.GetTimestamp();
            using var rendered = pipeline.Render(interactive);
            return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
    }

    private static double Median(double[] values)
    {
        var sorted = values.Order().ToArray();
        return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
    }
}

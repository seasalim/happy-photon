using System.Diagnostics;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgxCrossingPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public AgxCrossingPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void FusedPass_MeetsPreviewBudgetAndReportsFullImageTime()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run AgX performance diagnostics.");
#if DEBUG
        Assert.Skip("Run AgX performance diagnostics in Release.");
#endif

        var crossing = new AgxCrossing(
            AgxToneEnginePropertyTests.Parameters(contrast: 25));
        var previewMedian = Measure(crossing, 1600 * 1600);
        var fullImageMedian = Measure(crossing, 6000 * 4000);

        _output.WriteLine(
            $"AgX fused pass, 1600x1600 (2.56 MP): " +
            $"{previewMedian:F1} ms median (1 warm-up + 5 runs).");
        _output.WriteLine(
            $"AgX fused pass, 6000x4000 (24 MP): " +
            $"{fullImageMedian:F1} ms median (informational).");
        Assert.True(
            previewMedian <= 60,
            $"The 2.56 MP AgX fused pass took {previewMedian:F1} ms; budget is 60 ms.");
    }

    private static double Measure(AgxCrossing crossing, int pixelCount)
    {
        var source = CreateSamples(pixelCount);
        var destination = new ushort[source.Length];
        crossing.Apply(source, destination);

        var samples = new double[5];
        for (var iteration = 0; iteration < samples.Length; iteration++)
        {
            var stopwatch = Stopwatch.StartNew();
            crossing.Apply(source, destination);
            stopwatch.Stop();
            samples[iteration] = stopwatch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(samples);
        GC.KeepAlive(destination);
        return samples[samples.Length / 2];
    }

    private static ushort[] CreateSamples(int pixelCount)
    {
        var samples = new ushort[checked(pixelCount * 3)];
        var state = 0x9E3779B9u;
        for (var index = 0; index < samples.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            samples[index] = (ushort)state;
        }
        return samples;
    }
}

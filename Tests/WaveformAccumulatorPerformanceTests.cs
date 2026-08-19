using System.Diagnostics;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WaveformAccumulatorPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public WaveformAccumulatorPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void FullGridMedian_WhenEnabled()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run the waveform performance diagnostic.");

        const int width = 1024;
        const int height = 1024;
        var setup = Stopwatch.StartNew();
        var rgb = new ushort[width * height * 3];
        for (var offset = 0; offset < rgb.Length; offset++)
        {
            rgb[offset] = (ushort)(offset * 7919);
        }
        setup.Stop();

        _ = WaveformAccumulator.Accumulate(rgb, width, height);
        var samples = new double[9];
        ushort observed = 0;
        for (var index = 0; index < samples.Length; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            var waveform = WaveformAccumulator.Accumulate(rgb, width, height);
            stopwatch.Stop();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
            observed ^= waveform.Luminance[index];
        }

        Array.Sort(samples);
        var median = samples[samples.Length / 2];
        _output.WriteLine(
            $"1024x1024 Q16 RGB setup: {setup.Elapsed.TotalMilliseconds:F2} ms; " +
            $"waveform accumulator median: {median:F2} ms; observed: {observed}.");
        Assert.True(
            median <= 5,
            $"Waveform accumulator median was {median:F2} ms.");
    }
}

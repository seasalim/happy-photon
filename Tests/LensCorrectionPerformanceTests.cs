using System.Diagnostics;
using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LensCorrectionPerformanceTests
{
    private const int Samples = 5;
    private readonly ITestOutputHelper _output;

    public LensCorrectionPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public async Task X30QualifiedDistortionStaysWithinDecodeBudgets()
    {
        Assert.SkipWhen(
            !string.Equals(
                typeof(LensCorrectionPerformanceTests).Assembly
                    .GetCustomAttribute<AssemblyConfigurationAttribute>()?
                    .Configuration,
                "Release",
                StringComparison.OrdinalIgnoreCase),
            "The RAF lens performance gate is calibrated for Release builds.");
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_RAF_LENS_PERF") != "1",
            "Set HAPPY_PHOTON_RAF_LENS_PERF=1 to run the RAF lens gate.");

        var file = new ImageFile(GoldenTestPaths.Asset("fujifilm-x30.raf"));
        var inactive = new BaseDecodeSettings(
            HlReconstructionMode.Clip,
            FbddMode.Off,
            Distortion: false,
            ChromaticAberration: false,
            Vignetting: false);
        var active = inactive with { Distortion = true };
        var loader = new RawBaseLoader();
        using (loader.LoadPreviewBase(file, inactive, CancellationToken.None)) { }
        using (loader.LoadPreviewBase(file, active, CancellationToken.None)) { }
        using (loader.LoadFullBase(file, inactive, CancellationToken.None)) { }
        using (loader.LoadFullBase(file, active, CancellationToken.None)) { }

        var preview = await MeasurePairs(settings => loader.LoadPreviewBase(
            file, settings, CancellationToken.None), inactive, active);
        var full = await MeasurePairs(settings => loader.LoadFullBase(
            file, settings, CancellationToken.None), inactive, active);

        _output.WriteLine(
            $"X30 five-pair preview inactive/active={preview.InactiveMs:F1}/" +
            $"{preview.ActiveMs:F1} ms ({preview.TimeRatio:P1}); " +
            $"peak WS={preview.InactivePeakBytes}/{preview.ActivePeakBytes} " +
            $"({preview.MemoryRatio:P1}).");
        _output.WriteLine(
            $"X30 five-pair full inactive/active={full.InactiveMs:F1}/" +
            $"{full.ActiveMs:F1} ms ({full.TimeRatio:P1}); " +
            $"peak WS={full.InactivePeakBytes}/{full.ActivePeakBytes} " +
            $"({full.MemoryRatio:P1}).");

        Assert.True(preview.TimeRatio <= 1.15,
            $"Active preview ratio {preview.TimeRatio:P1} exceeds +15%.");
        Assert.True(full.TimeRatio <= 1.20,
            $"Active full ratio {full.TimeRatio:P1} exceeds +20%.");
        Assert.True(Math.Max(preview.MemoryRatio, full.MemoryRatio) <= 1.10,
            "Active peak working set exceeds +10%.");
    }

    private static async Task<PairReport> MeasurePairs(
        Func<BaseDecodeSettings, BaseImage?> decode,
        BaseDecodeSettings inactive,
        BaseDecodeSettings active)
    {
        var inactiveSamples = new List<Measurement>();
        var activeSamples = new List<Measurement>();
        for (var index = 0; index < Samples; index++)
        {
            if ((index & 1) == 0)
            {
                inactiveSamples.Add(await Measure(() => decode(inactive)));
                activeSamples.Add(await Measure(() => decode(active)));
            }
            else
            {
                activeSamples.Add(await Measure(() => decode(active)));
                inactiveSamples.Add(await Measure(() => decode(inactive)));
            }
        }
        return new PairReport(
            Median(inactiveSamples.Select(sample => sample.ElapsedMs)),
            Median(activeSamples.Select(sample => sample.ElapsedMs)),
            Median(inactiveSamples.Select(sample => (double)sample.PeakBytes)),
            Median(activeSamples.Select(sample => (double)sample.PeakBytes)));
    }

    private static async Task<Measurement> Measure(Func<BaseImage?> decode)
    {
        Collect();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var peak = process.WorkingSet64;
        using var stop = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                process.Refresh();
                peak = Math.Max(peak, process.WorkingSet64);
                await Task.Delay(5);
            }
        });
        var stopwatch = Stopwatch.StartNew();
        using var image = decode() ??
            throw new InvalidOperationException("X30 decode failed.");
        stopwatch.Stop();
        stop.Cancel();
        await sampler;
        return new Measurement(stopwatch.Elapsed.TotalMilliseconds, peak);
    }

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.Order().ToArray();
        return ordered[ordered.Length / 2];
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private readonly record struct Measurement(double ElapsedMs, long PeakBytes);

    private sealed record PairReport(
        double InactiveMs,
        double ActiveMs,
        double InactivePeakBytes,
        double ActivePeakBytes)
    {
        internal double TimeRatio => ActiveMs / InactiveMs;
        internal double MemoryRatio => ActivePeakBytes / InactivePeakBytes;
    }
}

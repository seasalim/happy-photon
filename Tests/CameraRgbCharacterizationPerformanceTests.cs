using System.Diagnostics;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CameraRgbCharacterizationPerformanceTests
{
    private const int SampleCount = 5;
    private const int SamplingIntervalMilliseconds = 10;
    private const long TransientMemoryBudget = 4L * 1024 * 1024;

    private readonly ITestOutputHelper _output;

    public CameraRgbCharacterizationPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public async Task ImportDelta_StaysWithinR5aBudgets_WhenEnabled()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_R5A_PERF") != "1",
            "Set HAPPY_PHOTON_R5A_PERF=1 to run the R5a import gate.");

        var preview = await MeasureCase(halfSize: true);
        var full = await MeasureCase(halfSize: false);
        Report("preview", preview);
        Report("full", full);

        Assert.True(
            preview.AddedMilliseconds <= 30,
            $"Preview characterization added {preview.AddedMilliseconds:F1} ms; " +
            "budget is 30 ms.");
        Assert.True(
            full.AddedMilliseconds <= 100,
            $"Full characterization added {full.AddedMilliseconds:F1} ms; " +
            "budget is 100 ms.");
        Assert.True(
            preview.AddedPeakPrivateBytes <= TransientMemoryBudget,
            $"Preview characterization added " +
            $"{preview.AddedPeakPrivateBytes / 1048576.0:F1} MiB private peak.");
        Assert.True(
            full.AddedPeakPrivateBytes <= TransientMemoryBudget,
            $"Full characterization added " +
            $"{full.AddedPeakPrivateBytes / 1048576.0:F1} MiB private peak.");
    }

    private static async Task<ImportDelta> MeasureCase(bool halfSize)
    {
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "canon-eos-6d-iso-6400.cr2");
        using var context = LibRawContext.Open(path);
        context.Unpack();
        var facts = RawCameraFactSnapshot.Copy(context.GetCameraFacts());
        var characterization = CameraRgbCharacterization.Create(facts);
        context.ConfigureOutput(LibRawOutputConfiguration.LinearCameraNative(
            LibRawHighlightMode.Clip,
            LibRawFbddMode.Off,
            halfSize));
        context.Process();
        using var processed = context.MakeProcessedImage();
        var width = checked((int)processed.Description.Width);
        var height = checked((int)processed.Description.Height);

        using (CameraRgbCharacterization.Passthrough.ImportRgb16(
            processed.AsSpan(), width, height)) { }
        using (characterization.ImportRgb16(
            processed.AsSpan(), width, height)) { }

        var baseline = MeasureElapsed(() =>
            CameraRgbCharacterization.Passthrough.ImportRgb16(
                processed.AsSpan(), width, height));
        var characterized = MeasureElapsed(() =>
            characterization.ImportRgb16(processed.AsSpan(), width, height));
        var baselinePeak = await MeasurePeak(() =>
            CameraRgbCharacterization.Passthrough.ImportRgb16(
                processed.AsSpan(), width, height));
        var characterizedPeak = await MeasurePeak(() =>
            characterization.ImportRgb16(processed.AsSpan(), width, height));
        return new ImportDelta(
            width,
            height,
            baseline,
            characterized,
            Math.Max(0, characterized - baseline),
            baselinePeak,
            characterizedPeak,
            Math.Max(0, characterizedPeak - baselinePeak));
    }

    private static double MeasureElapsed(Func<MagickImage> operation)
    {
        var samples = new double[SampleCount];
        for (var index = 0; index < samples.Length; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            using (operation()) { }
            stopwatch.Stop();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }

        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static async Task<long> MeasurePeak(Func<MagickImage> operation)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var baseline = process.PrivateMemorySize64;
        var peak = baseline;
        var task = Task.Run(operation);
        while (!task.IsCompleted)
        {
            await Task.Delay(SamplingIntervalMilliseconds);
            process.Refresh();
            peak = Math.Max(peak, process.PrivateMemorySize64);
        }

        using var image = await task;
        process.Refresh();
        peak = Math.Max(peak, process.PrivateMemorySize64);
        return Math.Max(0, peak - baseline);
    }

    private void Report(string name, ImportDelta value) =>
        _output.WriteLine(
            $"{name} {value.Width}x{value.Height}: direct " +
            $"{value.BaselineMilliseconds:F1} ms, characterized " +
            $"{value.CharacterizedMilliseconds:F1} ms, delta " +
            $"{value.AddedMilliseconds:F1} ms; private peaks " +
            $"{value.BaselinePeakPrivateBytes / 1048576.0:F1}/" +
            $"{value.CharacterizedPeakPrivateBytes / 1048576.0:F1} MiB, " +
            $"delta {value.AddedPeakPrivateBytes / 1048576.0:F1} MiB.");

    private sealed record ImportDelta(
        int Width,
        int Height,
        double BaselineMilliseconds,
        double CharacterizedMilliseconds,
        double AddedMilliseconds,
        long BaselinePeakPrivateBytes,
        long CharacterizedPeakPrivateBytes,
        long AddedPeakPrivateBytes);
}

using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class AgxPerformanceGateTests
{
    private async Task<(ExportMeasurement Neutral, ExportMeasurement Active)>
        MeasureThreeVariantNrPair(OutputColorSpace target, bool chroma)
    {
        var neutralFile = new ImageFile(
            GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2"));
        var activeFile = new ImageFile(neutralFile.FilePath)
        {
            EditSettings = CreateNoiseReductionSettings(chroma)
        };
        var neutralSettings = CreateVariantSettings(
            target,
            Path.Combine(
                _output.Path,
                $"variants-{target}-{(chroma ? "chroma" : "luma")}-nr-neutral"));
        var activeSettings = CreateVariantSettings(
            target,
            Path.Combine(
                _output.Path,
                $"variants-{target}-{(chroma ? "chroma" : "luma")}-nr-50"));
        var service = CreateExportService();
        return await MeasureExportPairAsync(
            () => ExportOne(service, neutralFile, neutralSettings),
            () => ExportOne(service, activeFile, activeSettings));
    }

    private async Task<(ExportMeasurement Neutral, ExportMeasurement Active)>
        MeasureStandardNrExportPair(bool chroma)
    {
        var neutralFile = new ImageFile(
            GoldenTestPaths.Asset("srgb-reference.jpg"));
        var activeFile = new ImageFile(neutralFile.FilePath)
        {
            EditSettings = CreateNoiseReductionSettings(chroma)
        };
        var neutralSettings = CreateStandardSettings(
            Path.Combine(
                _output.Path,
                $"standard-{(chroma ? "chroma" : "luma")}-nr-neutral"));
        var activeSettings = CreateStandardSettings(
            Path.Combine(
                _output.Path,
                $"standard-{(chroma ? "chroma" : "luma")}-nr-50"));
        var service = CreateExportService();
        return await MeasureExportPairAsync(
            () => ExportOne(service, neutralFile, neutralSettings),
            () => ExportOne(service, activeFile, activeSettings));
    }

    private static ExportSettings CreateVariantSettings(
        OutputColorSpace target,
        string outputFolder) => new()
    {
        OutputFolder = outputFolder,
        Format = ExportFormat.Jpeg,
        Quality = 85,
        OutputColorSpace = target,
        ExportWeb = true,
        ExportSmall = true,
        WebMaxSize = 2048,
        SmallMaxSize = 1024
    };

    private static ExportSettings CreateStandardSettings(string outputFolder) =>
        new()
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Jpeg,
            Quality = 85,
            OutputSharpening = OutputSharpeningMode.Off
        };

    private async Task ExportOne(
        ImageExportService service,
        ImageFile file,
        ExportSettings settings)
    {
        PrepareUniqueExport(settings);
        var result = await service.ExportBatchAsync([file], settings);
        Assert.True(result.ExportedCount == 1,
            string.Join("; ", result.FailedTargets.Select(target =>
                $"{target.Recipe}: {target.FailureReason}")));
    }

    private static (double Neutral, double Active) MeasurePair(
        Func<IDisposable> neutral,
        Func<IDisposable> active)
    {
        using (neutral()) { }
        using (active()) { }
        var neutralSamples = new double[SampleCount];
        var activeSamples = new double[SampleCount];
        for (var index = 0; index < SampleCount; index++)
        {
            if ((index & 1) == 0)
            {
                neutralSamples[index] = MeasureOne(neutral);
                activeSamples[index] = MeasureOne(active);
            }
            else
            {
                activeSamples[index] = MeasureOne(active);
                neutralSamples[index] = MeasureOne(neutral);
            }
        }
        return (Median(neutralSamples), Median(activeSamples));
    }

    private static double MeasureOne(Func<IDisposable> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        using (operation()) { }
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static async Task<(ExportMeasurement Neutral, ExportMeasurement Active)>
        MeasureExportPairAsync(Func<Task> neutral, Func<Task> active)
    {
        await neutral();
        await active();
        var neutralElapsed = new double[SampleCount];
        var activeElapsed = new double[SampleCount];
        var neutralPeaks = new long[SampleCount];
        var activePeaks = new long[SampleCount];
        for (var index = 0; index < SampleCount; index++)
        {
            if ((index & 1) == 0)
            {
                (neutralElapsed[index], neutralPeaks[index]) =
                    await MeasureExportSampleAsync(neutral);
                (activeElapsed[index], activePeaks[index]) =
                    await MeasureExportSampleAsync(active);
            }
            else
            {
                (activeElapsed[index], activePeaks[index]) =
                    await MeasureExportSampleAsync(active);
                (neutralElapsed[index], neutralPeaks[index]) =
                    await MeasureExportSampleAsync(neutral);
            }
        }

        Array.Sort(neutralPeaks);
        Array.Sort(activePeaks);
        return (
            new ExportMeasurement(
                Median(neutralElapsed), neutralPeaks[neutralPeaks.Length / 2]),
            new ExportMeasurement(
                Median(activeElapsed), activePeaks[activePeaks.Length / 2]));
    }

    private static async Task<(double ElapsedMs, long PeakPrivateBytes)>
        MeasureExportSampleAsync(Func<Task> operation)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var baseline = process.PrivateMemorySize64;
        var peak = baseline;
        var stopwatch = Stopwatch.StartNew();
        var export = operation();
        while (!export.IsCompleted)
        {
            await Task.Delay(SamplingIntervalMilliseconds);
            process.Refresh();
            peak = Math.Max(peak, process.PrivateMemorySize64);
        }
        await export;
        stopwatch.Stop();
        process.Refresh();
        peak = Math.Max(peak, process.PrivateMemorySize64);
        return (
            stopwatch.Elapsed.TotalMilliseconds,
            Math.Max(0, peak - baseline));
    }
}

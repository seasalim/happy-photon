using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportPipelinePerformanceTests
{
    private readonly ITestOutputHelper _output;

    public ExportPipelinePerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public async Task FullRawExport_ReportsLatencyAndPeakMemory()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run export performance diagnostics.");
        var outputFolder = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonExportPerf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputFolder);
        try
        {
            var rawPath =
                Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF_RAW") ??
                Path.Combine(
                    GoldenTestPaths.AssetDirectory,
                    "fujifilm-x30.raf");
            var measurement = await RawExportPerformanceMeasurement.MeasureAsync(
                rawPath,
                outputFolder);
            _output.WriteLine(
                $"Full RAW export with chroma NR 100 {measurement.Width}x{measurement.Height}: " +
                $"{measurement.Elapsed.TotalSeconds:F2} s, " +
                $"after decode " +
                $"{measurement.AfterDecodePrivateBytes / 1048576.0:F1} MiB, " +
                $"peak private-memory delta " +
                $"{measurement.PeakPrivateBytes / 1048576.0:F1} MiB.");
        }
        finally
        {
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    [Theory]
    [InlineData(OutputColorSpace.Srgb)]
    [InlineData(OutputColorSpace.DisplayP3)]
    public async Task ActiveEffects_StayWithinFrozenExportBudgets(
        OutputColorSpace outputColorSpace)
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run export performance diagnostics.");
        var root = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonEffectsExportPerf_{Guid.NewGuid():N}");
        try
        {
            var rawPath =
                Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF_RAW") ??
                Path.Combine(
                    GoldenTestPaths.AssetDirectory,
                    "fujifilm-x30.raf");
            var off = await RawExportPerformanceMeasurement.MeasureAsync(
                rawPath,
                Path.Combine(root, "off"),
                outputColorSpace: outputColorSpace);
            var active = await RawExportPerformanceMeasurement.MeasureAsync(
                rawPath,
                Path.Combine(root, "active"),
                new EffectsSettings
                {
                    Vignette = -50,
                    Grain = 50,
                    GrainSize = GrainSize.Medium
                },
                outputColorSpace);
            var elapsedDelta = active.Elapsed - off.Elapsed;
            var elapsedBudget = TimeSpan.FromMilliseconds(Math.Max(
                off.Elapsed.TotalMilliseconds * 0.05,
                500));
            var memoryDelta = Math.Max(
                0,
                active.PeakPrivateBytes - off.PeakPrivateBytes);
            var frameBudget = checked((long)active.Width * active.Height * 3 * 2);
            _output.WriteLine(
                $"{outputColorSpace} effects-off {off.Elapsed.TotalSeconds:F2} s; " +
                $"active {active.Elapsed.TotalSeconds:F2} s; delta " +
                $"{elapsedDelta.TotalMilliseconds:F0} ms; incremental peak " +
                $"{memoryDelta / 1048576.0:F1} MiB.");

            Assert.True(
                elapsedDelta <= elapsedBudget,
                $"Active effects added {elapsedDelta.TotalMilliseconds:F0} ms " +
                $"(budget {elapsedBudget.TotalMilliseconds:F0} ms).");
            Assert.True(
                memoryDelta <= frameBudget,
                $"Active effects added {memoryDelta / 1048576.0:F1} MiB " +
                $"(one-frame budget {frameBudget / 1048576.0:F1} MiB).");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

}

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

}

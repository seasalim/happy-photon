using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
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
            var file = new ImageFile(rawPath)
            {
                EditSettings = new EditSettings
                {
                    Detail = new DetailSettings { ChromaNr = 100 }
                }
            };
            var loader = new MeasuringLoader(new BaseLoaderRouter(
                new RawBaseLoader(),
                new StandardBaseLoader()));
            var service = new ImageExportService(
                new RenderPipeline(),
                loader,
                new ExportMetadataService());
            var settings = new ExportSettings
            {
                OutputFolder = outputFolder,
                Format = ExportFormat.Jpeg,
                Quality = 85
            };
            var process = Process.GetCurrentProcess();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            process.Refresh();
            var baseline = process.PrivateMemorySize64;
            var peak = baseline;
            var stopwatch = Stopwatch.StartNew();

            var export = service.ExportBatchAsync([file], settings);
            while (!export.IsCompleted)
            {
                await Task.Delay(10);
                process.Refresh();
                peak = Math.Max(peak, process.PrivateMemorySize64);
            }

            Assert.Equal(1, await export);
            stopwatch.Stop();
            process.Refresh();
            peak = Math.Max(peak, process.PrivateMemorySize64);
            var info = new ImageMagick.MagickImageInfo(
                Path.Combine(
                    outputFolder,
                    $"{Path.GetFileNameWithoutExtension(rawPath)}.jpg"));
            _output.WriteLine(
                $"Full RAW export with chroma NR 100 {info.Width}x{info.Height}: " +
                $"{stopwatch.Elapsed.TotalSeconds:F2} s, " +
                $"after decode " +
                $"{(loader.AfterFullDecodeBytes - baseline) / 1048576.0:F1} MiB, " +
                $"peak private-memory delta " +
                $"{(peak - baseline) / 1048576.0:F1} MiB.");
        }
        finally
        {
            Directory.Delete(outputFolder, recursive: true);
        }
    }

    private sealed class MeasuringLoader(IBaseImageLoader inner)
        : IBaseImageLoader
    {
        public long AfterFullDecodeBytes { get; private set; }

        public bool CanLoad(ImageFile file) => inner.CanLoad(file);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            inner.LoadPreviewBase(file, decode, cancellationToken);

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            var result = inner.LoadFullBase(
                file,
                decode,
                cancellationToken);
            var process = Process.GetCurrentProcess();
            process.Refresh();
            AfterFullDecodeBytes = process.PrivateMemorySize64;
            return result;
        }
    }
}

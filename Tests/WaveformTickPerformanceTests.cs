using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class WaveformTickPerformanceTests
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public WaveformTickPerformanceTests(
        AvaloniaTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [WindowsFact]
    public async Task WaveformActiveSliderTickLatency_WhenEnabled()
    {
        _fixture.RequireWindows();
        if (Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1")
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonWaveformPerf_{Guid.NewGuid():N}");
        try
        {
            using var catalog = new CatalogService(root);
            await catalog.InitializeAsync();
            var file = new ImageFile(
                GoldenTestPaths.Asset("display-p3-reference.jpg"));
            await using var service = CreateService(catalog);
            var settings = new EditSettings
            {
                Exposure = 0.5,
                Saturation = 10
            };
            using (var warmup = await service.ApplyEditsToPreviewArtifactsAsync(
                file,
                settings,
                ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
                skipHistogram: false,
                ClippingOverlaySide.None))
            {
                Assert.NotNull(warmup.Bitmap);
            }

            var histogramOnly = await MeasureWaveformTick(
                service,
                file,
                settings,
                computeWaveform: false);
            var waveformActive = await MeasureWaveformTick(
                service,
                file,
                settings,
                computeWaveform: true);
            _output.WriteLine(
                $"JPEG waveform-active slider-tick " +
                $"v{RenderPipeline.Version} render: " +
                $"{waveformActive:F1} ms median " +
                $"(histogram-only {histogramOnly:F1} ms, " +
                $"delta {waveformActive - histogramOnly:F1} ms)");
            Assert.True(
                waveformActive <= 150,
                $"JPEG waveform-active slider tick exceeded 150 ms: " +
                $"{waveformActive:F1} ms median.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private async Task<double> MeasureWaveformTick(
        PreviewService service,
        ImageFile file,
        EditSettings settings,
        bool computeWaveform)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var samples = new List<double>();
        for (var index = 0; index < 5; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            using var artifacts = await service.ApplyEditsToPreviewArtifactsAsync(
                file,
                settings,
                ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
                skipHistogram: false,
                ClippingOverlaySide.None,
                computeWaveform: computeWaveform);
            stopwatch.Stop();
            Assert.NotNull(artifacts.Bitmap);
            if (computeWaveform)
            {
                Assert.NotNull(artifacts.Histogram.Waveform);
            }
            else
            {
                Assert.Null(artifacts.Histogram.Waveform);
            }
            samples.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        var ordered = samples.Order().ToArray();
        return ordered[ordered.Length / 2];
    }

    private static PreviewService CreateService(CatalogService catalog) =>
        new(
            catalog,
            new BaseLoaderRouter(
                new RawBaseLoader(),
                new StandardBaseLoader()),
            new RenderPipeline(),
            new PreviewCacheService(catalog),
            new RenderedThumbnailCacheService(catalog),
            createRenderedThumbnail: true);
}

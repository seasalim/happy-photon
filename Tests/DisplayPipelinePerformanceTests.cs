using System.Diagnostics;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class DisplayPipelinePerformanceTests
{
    private static readonly string ProfilePath = Path.Combine(
        GoldenTestPaths.AssetDirectory,
        "softproof",
        "softproof-p3-gamma22.icc");
    private readonly AvaloniaTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DisplayPipelinePerformanceTests(
        AvaloniaTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [WindowsFact]
    public async Task ManagedDisplaySliderTick_AddsAtMostSixMilliseconds()
    {
        _fixture.RequireWindows();
#if DEBUG
        Assert.Skip("Run the FULL_CPU display performance gate in Release configuration.");
#endif
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_FULL_CPU") != "1",
            "Set HAPPY_PHOTON_FULL_CPU=1 to run the integrated display gate.");
        PerfEnvironment.AssertFullCpu();

        var root = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonDisplayPerf_{Guid.NewGuid():N}");
        try
        {
            using var catalog = new CatalogService(root);
            await catalog.InitializeAsync();
            await using var service = CreateService(catalog);
            var file = new ImageFile(GoldenTestPaths.Asset("canon-eos-350d.cr2"));
            var managed = new DisplayColorManagementService(new FakePlatform(new(
                "managed", ProfilePath, DisplayAcmState.Off))).Resolve(1);

            _ = await MeasureTick(
                service, file, new EditSettings { Exposure = 0.1 },
                managed);
            var identitySamples = new List<double>(5);
            var managedSamples = new List<double>(5);
            var deriveSamples = new List<double>(5);
            for (var index = 0; index < 5; index++)
            {
                var settings = new EditSettings
                {
                    Exposure = 0.2 + index * 0.03,
                    Saturation = 10,
                };
                var tick = await MeasureTick(service, file, settings, managed);
                identitySamples.Add(tick.Identity);
                managedSamples.Add(tick.Managed);
                deriveSamples.Add(tick.Derive);
            }

            var identityMedian = Median(identitySamples);
            var managedMedian = Median(managedSamples);
            _output.WriteLine(
                $"1600px slider tick: identity={identityMedian:F2} ms; " +
                $"managed={managedMedian:F2} ms; delta={managedMedian - identityMedian:F2} ms; " +
                $"derive={Median(deriveSamples):F2} ms");
            Assert.True(
                managedMedian <= identityMedian + 6,
                $"Managed display added {managedMedian - identityMedian:F2} ms (budget 6 ms).");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(double Identity, double Managed, double Derive)> MeasureTick(
        PreviewService service,
        ImageFile file,
        EditSettings settings,
        DisplayTransformSnapshot transform)
    {
        var stopwatch = Stopwatch.StartNew();
        var (canonical, _) = await service.ApplyEditsToPreviewAsync(
            file, settings, skipHistogram: false);
        Assert.NotNull(canonical);
        Assert.Equal(
            BaseImage.InteractivePreviewMaxDimension,
            Math.Max(canonical!.PixelSize.Width, canonical.PixelSize.Height));
        var identity = stopwatch.Elapsed;
        var displayed = transform.Derive(canonical, DisplaySourceColorSpace.Srgb);
        var derive = stopwatch.Elapsed - identity;
        stopwatch.Stop();
        if (!ReferenceEquals(displayed, canonical)) displayed.Dispose();
        canonical.Dispose();
        return (
            identity.TotalMilliseconds,
            stopwatch.Elapsed.TotalMilliseconds,
            derive.TotalMilliseconds);
    }

    private static double Median(IEnumerable<double> samples)
    {
        var ordered = samples.Order().ToArray();
        return ordered[ordered.Length / 2];
    }

    private static PreviewService CreateService(CatalogService catalog) => new(
        catalog,
        new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
        new RenderPipeline(),
        new PreviewCacheService(catalog),
        new RenderedThumbnailCacheService(catalog),
        createRenderedThumbnail: false);

    private sealed class FakePlatform(DisplayPlatformResult result) : IDisplayProfilePlatform
    {
        public DisplayPlatformResult Resolve(nint windowHandle) => result;
    }
}

using System.Diagnostics;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawPreviewStagePerformanceTests
{
    private readonly ITestOutputHelper _output;

    public RawPreviewStagePerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void FixtureStageBreakdown_WhenEnabled()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to profile RAW preview stages.");

        foreach (var name in new[]
                 {
                     "nikon-d70-burst-1.nef",
                     "nikon-d300-colorchecker.nef",
                     "canon-eos-350d.cr2",
                     "canon-eos-6d-iso-6400.cr2",
                     "pentax-k-r.dng",
                     "fujifilm-x30.raf"
                 })
        {
            MeasureFixture(name);
        }
    }

    [Fact]
    public async Task ConcurrentDecodeCost_WhenEnabled()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to profile concurrent RAW decodes.");

        foreach (var name in new[]
                 {
                     "nikon-d300-colorchecker.nef",
                     "canon-eos-6d-iso-6400.cr2"
                 })
        {
            var path = GoldenTestPaths.Asset(name);
            var baseline = MeasureLoad(path);
            using var start = new Barrier(2);
            var background = Task.Run(() =>
            {
                start.SignalAndWait();
                return MeasureLoad(path);
            });
            var foreground = Task.Run(() =>
            {
                start.SignalAndWait();
                return MeasureLoad(path);
            });
            var results = await Task.WhenAll(background, foreground);
            _output.WriteLine(
                $"fixture={name}; single={baseline:F1}; " +
                $"concurrent_foreground={results[1]:F1}; " +
                $"concurrent_background={results[0]:F1}; " +
                $"foreground_ratio={results[1] / baseline:F2}");
        }
    }

    [Fact]
    public void ExternalFileStageBreakdown_WhenEnabled()
    {
        var path = Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_PERF_RAW");
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(path),
            "Set HAPPY_PHOTON_PERF_RAW to an approved local RAW path.");
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.True(new ImageFile(path).IsRaw, "The supplied path must be RAW.");
        Assert.Equal(
            SourceAvailability.AvailableLocally,
            new SourceAvailabilityService().GetAvailability(path));

        MeasureFixture(path);
    }

    private void MeasureFixture(string name)
    {
        var path = Path.IsPathFullyQualified(name)
            ? name
            : GoldenTestPaths.Asset(name);
        var stages = MeasureNativeStages(path);
        var file = new ImageFile(path);
        var decode = BaseDecodeSettings.Default;

        var withoutHistogram = Measure(() =>
        {
            var loader = new RawBaseLoader(
                isAvailable: true,
                rawHistogramSampler: (_, _) => null);
            using var outcome = loader.LoadPreviewBaseWithOutcome(
                file,
                decode,
                CancellationToken.None).Pair;
            Assert.NotNull(outcome);
        });

        var totalWatch = Stopwatch.StartNew();
        var loaded = new RawBaseLoader().LoadPreviewBaseWithOutcome(
            file,
            decode,
            CancellationToken.None);
        totalWatch.Stop();
        using var pair = loaded.Pair;
        Assert.NotNull(pair);

        var render = Measure(() =>
        {
            using var result = new RenderPipeline().Render(new RenderRequest(
                pair.Interactive,
                file.EditSettings,
                RenderIntent.Preview,
                BaseImage.InteractivePreviewMaxDimension,
                new RenderOptions(
                    ComputeStats: true,
                    ComputeOverlayMasks: false,
                    OverlaySides: ClippingOverlaySide.None,
                    ComputeHistogram: true,
                    ComputeWaveform: true,
                    PreparePreviewPixels: true))
            {
                SourceSaturation = loaded.Analysis.SourceSaturation
            });
        });

        var variants = MeasureProcessVariants(path);

        _output.WriteLine(
            $"fixture={name}; mib={new FileInfo(path).Length / 1048576.0:F1}; " +
            $"raw={stages.RawWidth}x{stages.RawHeight}; " +
            $"processed={stages.ProcessedWidth}x{stages.ProcessedHeight}; " +
            $"open={stages.OpenMs:F1}; headers_thumb={stages.HeadersMs:F1}; " +
            $"unpack={stages.UnpackMs:F1}; histogram={stages.HistogramMs:F1}; " +
            $"process={stages.ProcessMs:F1}; make_import={stages.ImportMs:F1}; " +
            $"exposure={stages.ExposureMs:F1}; pair_resize={stages.PairMs:F1}; " +
            $"loader_no_hist={withoutHistogram:F1}; " +
            $"loader_total={totalWatch.Elapsed.TotalMilliseconds:F1}; " +
            $"render_scopes_pixels={render:F1}; " +
            $"process_clip={variants.ClipMs:F1}; " +
            $"process_blend={variants.BlendMs:F1}; " +
            $"process_fbdd_light={variants.FbddLightMs:F1}; " +
            $"process_fbdd_full={variants.FbddFullMs:F1}");
    }

    private static NativeStages MeasureNativeStages(string path)
    {
        var stopwatch = Stopwatch.StartNew();
        using var context = LibRawContext.Open(path);
        var open = stopwatch.Elapsed.TotalMilliseconds;

        var sensor = context.GetSensorIdentity();
        var dimensions = context.GetDimensions();
        _ = context.GetMetadata();
        var thumbnail = RawThumbnailReader.Read(context);
        var headers = stopwatch.Elapsed.TotalMilliseconds - open;

        context.Unpack();
        var unpack = stopwatch.Elapsed.TotalMilliseconds - open - headers;
        var facts = RawCameraFactSnapshot.Copy(context.GetCameraFacts());

        var histogramStart = stopwatch.Elapsed.TotalMilliseconds;
        _ = RawSensorHistogram.SampleWithSaturation(
            context,
            Math.Max(1, (checked((int)dimensions.VisibleWidth) + 1) / 2),
            Math.Max(1, (checked((int)dimensions.VisibleHeight) + 1) / 2),
            CancellationToken.None);
        var histogram = stopwatch.Elapsed.TotalMilliseconds - histogramStart;

        context.ConfigureOutput(RawBaseLoader.ConfigureOutput(
            BaseDecodeSettings.Default,
            preview: true,
            RawBaseLoader.IsMonochromeSensor(sensor)));
        var processStart = stopwatch.Elapsed.TotalMilliseconds;
        context.Process();
        var process = stopwatch.Elapsed.TotalMilliseconds - processStart;

        var importStart = stopwatch.Elapsed.TotalMilliseconds;
        uint processedWidth;
        uint processedHeight;
        using var processed = context.MakeProcessedImage();
        processedWidth = processed.Description.Width;
        processedHeight = processed.Description.Height;
        using var pixels = CameraRgbCharacterization.Create(facts).ImportRgb16(
            processed.AsSpan(),
            checked((int)processedWidth),
            checked((int)processedHeight),
            CancellationToken.None);
        var import = stopwatch.Elapsed.TotalMilliseconds - importStart;
        context.Recycle();

        var exposureStart = stopwatch.Elapsed.TotalMilliseconds;
        _ = PreviewExposureEstimator.Estimate(thumbnail, pixels, 0, path);
        var exposure = stopwatch.Elapsed.TotalMilliseconds - exposureStart;

        var pairStart = stopwatch.Elapsed.TotalMilliseconds;
        using var interactive = new MagickImage(pixels);
        BitmapConversionService.ResizeToMaxDimension(
            interactive,
            BaseImage.InteractivePreviewMaxDimension);
        using var large = new MagickImage(pixels);
        BitmapConversionService.ResizeToMaxDimension(
            large,
            BaseImage.LargePreviewMaxDimension);
        var pair = stopwatch.Elapsed.TotalMilliseconds - pairStart;

        return new NativeStages(
            dimensions.VisibleWidth,
            dimensions.VisibleHeight,
            processedWidth,
            processedHeight,
            open,
            headers,
            unpack,
            histogram,
            process,
            import,
            exposure,
            pair);
    }

    private static double Measure(Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        action();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static double MeasureLoad(string path) => Measure(() =>
    {
        var loaded = new RawBaseLoader().LoadPreviewBaseWithOutcome(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var pair = loaded.Pair;
        Assert.NotNull(pair);
    });

    private static ProcessVariants MeasureProcessVariants(string path) =>
        new(
            MeasureProcess(path, HlReconstructionMode.Clip, FbddMode.Off),
            MeasureProcess(path, HlReconstructionMode.Blend, FbddMode.Off),
            MeasureProcess(path, HlReconstructionMode.Clip, FbddMode.Light),
            MeasureProcess(path, HlReconstructionMode.Clip, FbddMode.Full));

    private static double MeasureProcess(
        string path,
        HlReconstructionMode highlight,
        FbddMode noiseReduction)
    {
        using var context = LibRawContext.Open(path);
        context.Unpack();
        context.ConfigureOutput(RawBaseLoader.ConfigureOutput(
            new BaseDecodeSettings(highlight, noiseReduction),
            preview: true));
        var stopwatch = Stopwatch.StartNew();
        context.Process();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private sealed record NativeStages(
        uint RawWidth,
        uint RawHeight,
        uint ProcessedWidth,
        uint ProcessedHeight,
        double OpenMs,
        double HeadersMs,
        double UnpackMs,
        double HistogramMs,
        double ProcessMs,
        double ImportMs,
        double ExposureMs,
        double PairMs);

    private sealed record ProcessVariants(
        double ClipMs,
        double BlendMs,
        double FbddLightMs,
        double FbddFullMs);
}

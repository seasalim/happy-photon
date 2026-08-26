using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class AgxPerformanceGateTests : IDisposable
{
    private const int SampleCount = 5;
    private const int SamplingIntervalMilliseconds = 10;

    private readonly TemporaryDirectory _output = new();

    [Fact]
    public async Task IntegratedGate_ReportsFrozenPerformanceCases()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run checkpoint-E performance.");
        var reportPath = Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_AGX_PERF_REPORT") ??
            throw new InvalidOperationException(
                "HAPPY_PHOTON_AGX_PERF_REPORT is required.");
        var target = ParseTarget(Environment.GetEnvironmentVariable(
            "HAPPY_PHOTON_AGX_PERF_TARGET"));

        var sliders = MeasureSliderTicks();
        var variants = await MeasureThreeVariants(target);
        var activeChromaVariants = await MeasureThreeVariants(
            target,
            "chroma",
            CreateActiveMixerSettings());
        var luminanceNrVariantPair = await MeasureThreeVariantPair(target);
        var standardPair = await MeasureStandardExportPair();
        var activeChromaPrivateDeltaBytes = Math.Max(
            0,
            activeChromaVariants.PeakPrivateBytes - variants.PeakPrivateBytes);
        File.WriteAllText(
            reportPath,
            JsonSerializer.Serialize(
                new
                {
                    protocol = new
                    {
                        warmups = 1,
                        samples = SampleCount,
                        statistic = "median",
                        pairedNeutralAndLuminanceNr = true,
                        memorySamplingIntervalMs =
                            SamplingIntervalMilliseconds
                    },
                    target,
                    sliders,
                    variants,
                    activeChromaVariants,
                    activeChromaPrivateDeltaBytes,
                    luminanceNrNeutralVariants = luminanceNrVariantPair.Neutral,
                    luminanceNrVariants = luminanceNrVariantPair.Active,
                    standard = standardPair.Neutral.ElapsedMs,
                    luminanceNrStandard = standardPair.Active.ElapsedMs
                },
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }) + Environment.NewLine);

        Assert.All(
            sliders.Where(value => value.ElapsedMs.HasValue),
            value => Assert.True(
                 value.ElapsedMs <= 150,
                $"{value.Fixture} slider tick took " +
                 $"{value.ElapsedMs:F1} ms; budget is 150 ms."));
        Assert.All(
            sliders.Where(value => value.NeutralElapsedMs.HasValue),
            value => Assert.True(
                value.ElapsedMs!.Value - value.NeutralElapsedMs!.Value <= 20,
                $"{value.Fixture} added " +
                $"{value.ElapsedMs.Value - value.NeutralElapsedMs.Value:F1} ms; " +
                "budget is 20 ms over neutral."));
        Assert.True(
            activeChromaPrivateDeltaBytes <= 16L * 1024 * 1024,
            $"Active chroma added " +
            $"{activeChromaPrivateDeltaBytes / 1024d / 1024d:F1} MiB private " +
             "memory; budget is 16 MiB.");
        AssertExportWallDelta(
            "three-variant RAW luminance NR 50",
            luminanceNrVariantPair.Neutral.ElapsedMs,
            luminanceNrVariantPair.Active.ElapsedMs,
            fullResolutionRenderCount: 3);
        AssertExportWallDelta(
            "standard luminance NR 50",
            standardPair.Neutral.ElapsedMs,
            standardPair.Active.ElapsedMs,
            fullResolutionRenderCount: 1);
    }

    private static void AssertExportWallDelta(
        string label,
        double neutralMs,
        double activeMs,
        int fullResolutionRenderCount)
    {
        var budget = Math.Max(
            neutralMs * 0.05,
            500 * fullResolutionRenderCount);
        Assert.True(
            activeMs - neutralMs <= budget,
            $"{label} added {activeMs - neutralMs:F1} ms; " +
            $"budget is {budget:F1} ms.");
    }

    private static SliderMeasurement[] MeasureSliderTicks()
    {
        var fixtures = new[]
        {
            "canon-eos-6d-iso-6400.cr2",
            "fujifilm-x30.raf",
            "srgb-reference.jpg",
            "iphone-14-pro-iso-1000.heic"
        };
        var loader = new BaseLoaderRouter(
            new RawBaseLoader(),
            new StandardBaseLoader());
        var pipeline = new RenderPipeline();
        var existing = fixtures.SelectMany<string, SliderMeasurement>(fixture =>
        {
            if (Path.GetExtension(fixture).Equals(
                    ".heic",
                    StringComparison.OrdinalIgnoreCase) &&
                MagickFormatInfo.Create(MagickFormat.Heic) is not
                    { SupportsReading: true })
            {
                return new[]
                {
                    new SliderMeasurement(
                        fixture,
                        null,
                        "ImageMagick has no HEIC reader"),
                    new SliderMeasurement(
                        $"{fixture} (active chroma)",
                        null,
                        "ImageMagick has no HEIC reader"),
                    new SliderMeasurement(
                        $"{fixture} (luminance NR 50)",
                        null,
                        "ImageMagick has no HEIC reader"),
                    new SliderMeasurement(
                        $"{fixture} (luminance NR 100)",
                        null,
                        "ImageMagick has no HEIC reader")
                };
            }

            using var baseImage = loader.LoadPreviewBase(
                new ImageFile(GoldenTestPaths.Asset(fixture)),
                BaseDecodeSettings.Default,
                CancellationToken.None) ??
                throw new InvalidOperationException(
                    $"Slider fixture did not decode: {fixture}.");
            var contrast = MeasureRender(
                pipeline,
                baseImage,
                new EditSettings { Contrast = 25 });
            var chroma = MeasureRender(
                pipeline,
                baseImage,
                CreateActiveMixerSettings());
            var pair50 = MeasureRenderPair(
                pipeline,
                baseImage,
                CreateLuminanceNrSettings(50));
            var pair100 = MeasureRenderPair(
                pipeline,
                baseImage,
                CreateLuminanceNrSettings(100));
            var measurements = new List<SliderMeasurement>
            {
                new SliderMeasurement(fixture, contrast, null),
                new SliderMeasurement($"{fixture} (active chroma)", chroma, null),
                new SliderMeasurement(
                    $"{fixture} (luminance NR 50)",
                    pair50.Active,
                    null,
                    pair50.Neutral),
                new SliderMeasurement(
                    $"{fixture} (luminance NR 100)",
                    pair100.Active,
                    null,
                    pair100.Neutral)
            };
            if (fixture == "canon-eos-6d-iso-6400.cr2")
            {
                measurements.Add(new SliderMeasurement(
                    $"{fixture} (projection-heavy S=+100)",
                    MeasureRender(
                        pipeline,
                        baseImage,
                        new EditSettings { Saturation = 100 }),
                    null));
            }
            return measurements;
        });
        var channels = new[]
        {
            MeasureAllChannelTicks(
                loader,
                pipeline,
                "canon-eos-6d-iso-6400.cr2"),
            MeasureAllChannelTicks(loader, pipeline, "srgb-reference.jpg")
        };
        return existing.Concat(channels).ToArray();
    }

    private static double MeasureRender(
        RenderPipeline pipeline,
        BaseImage baseImage,
        EditSettings settings) =>
        Measure(() => pipeline.Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            1600,
            new RenderOptions(false, false))));

    private static (double Neutral, double Active) MeasureRenderPair(
        RenderPipeline pipeline,
        BaseImage baseImage,
        EditSettings activeSettings) => MeasurePair(
            () => pipeline.Render(new RenderRequest(
                baseImage,
                new EditSettings(),
                RenderIntent.Preview,
                1600,
                new RenderOptions(false, false))),
            () => pipeline.Render(new RenderRequest(
                baseImage,
                activeSettings,
                RenderIntent.Preview,
                1600,
                new RenderOptions(false, false))));

    private static SliderMeasurement MeasureAllChannelTicks(
        IBaseImageLoader loader,
        RenderPipeline pipeline,
        string fixture)
    {
        using var baseImage = loader.LoadPreviewBase(
            new ImageFile(GoldenTestPaths.Asset(fixture)),
            BaseDecodeSettings.Default,
            CancellationToken.None) ??
            throw new InvalidOperationException(
                $"Channel-curve fixture did not decode: {fixture}.");
        var elapsed = MeasureCold(sample =>
        {
            var settings = CreateAllChannelSettings(sample);
            return pipeline.Render(new RenderRequest(
                baseImage,
                settings,
                RenderIntent.Preview,
                1600,
                new RenderOptions(false, false)));
        });
        return new SliderMeasurement(
            $"{fixture} (all channel curves, cold)",
            elapsed,
            null);
    }

    private static EditSettings CreateAllChannelSettings(int sample)
    {
        var offset = (sample + 2) * 0.01;
        return new EditSettings
        {
            Contrast = 25,
            CurveRed = CreateCurve(0.4, 0.58 + offset),
            CurveGreen = CreateCurve(0.5, 0.42 - offset),
            CurveBlue = CreateCurve(0.6, 0.68 + offset)
        };
    }

    private static CurveData CreateCurve(double x, double y)
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(x, y);
        return curve;
    }

    private static EditSettings CreateActiveMixerSettings()
    {
        var settings = new EditSettings
        {
            Saturation = 50,
            Vibrance = 50,
            Mixer = new ColorMixerSettings()
        };
        settings.Mixer.Orange.Hue = 35;
        settings.Mixer.Blue.Saturation = 40;
        settings.Mixer.Magenta.Luminance = -20;
        return settings;
    }

    private static EditSettings CreateLuminanceNrSettings(int value = 50) => new()
    {
        Detail = new DetailSettings { LuminanceNr = value }
    };

    private async Task<ExportMeasurement> MeasureThreeVariants(
        OutputColorSpace target,
        string label = "off",
        EditSettings? editSettings = null)
    {
        var file = new ImageFile(GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2"))
        {
            EditSettings = editSettings ?? new EditSettings()
        };
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(
                _output.Path,
                $"variants-{target}-{label}"),
            Format = ExportFormat.Jpeg,
            Quality = 85,
            OutputColorSpace = target,
            ExportWeb = true,
            ExportSmall = true,
            WebMaxSize = 2048,
            SmallMaxSize = 1024
        };
        var service = CreateExportService();
        return await MeasureExportAsync(async () =>
        {
            var result = await service.ExportBatchAsync([file], settings);
            Assert.Equal(1, result.ExportedCount);
        });
    }

    private static double Measure(Func<IDisposable> operation)
    {
        using (operation()) { }
        var samples = new double[SampleCount];
        for (var index = 0; index < samples.Length; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            using (operation()) { }
            stopwatch.Stop();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }
        return Median(samples);
    }

    private static double MeasureCold(Func<int, IDisposable> operation)
    {
        using (operation(-1)) { }
        var samples = new double[SampleCount];
        for (var index = 0; index < samples.Length; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            using (operation(index)) { }
            stopwatch.Stop();
            samples[index] = stopwatch.Elapsed.TotalMilliseconds;
        }
        return Median(samples);
    }

    private static async Task<ExportMeasurement> MeasureExportAsync(
        Func<Task> operation)
    {
        await operation();
        var elapsed = new double[SampleCount];
        var privatePeaks = new long[SampleCount];
        for (var index = 0; index < SampleCount; index++)
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
            elapsed[index] = stopwatch.Elapsed.TotalMilliseconds;
            privatePeaks[index] = Math.Max(0, peak - baseline);
        }

        Array.Sort(privatePeaks);
        return new ExportMeasurement(
            Median(elapsed),
            privatePeaks[privatePeaks.Length / 2]);
    }

    private static double Median(double[] samples)
    {
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static OutputColorSpace ParseTarget(string? value) => value switch
    {
        "srgb" => OutputColorSpace.Srgb,
        "display-p3" => OutputColorSpace.DisplayP3,
        _ => throw new InvalidOperationException(
            "HAPPY_PHOTON_AGX_PERF_TARGET must be " +
            "'srgb' or 'display-p3'.")
    };

    private static ImageExportService CreateExportService() =>
        new(
            new RenderPipeline(),
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
            new ExportMetadataService());

    public void Dispose() => _output.Dispose();

    private sealed record SliderMeasurement(
        string Fixture,
        double? ElapsedMs,
        string? SkipReason,
        double? NeutralElapsedMs = null);

    private sealed record ExportMeasurement(
        double ElapsedMs,
        long PeakPrivateBytes);
}

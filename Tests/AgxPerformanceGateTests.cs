using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgxPerformanceGateTests : IDisposable
{
    private const int SampleCount = 5;
    private const int SamplingIntervalMilliseconds = 10;

    private readonly string _output = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonCheckpointEPerf_{Guid.NewGuid():N}")).FullName;

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
        var standard = await MeasureStandardExport();
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
                        memorySamplingIntervalMs =
                            SamplingIntervalMilliseconds
                    },
                    target,
                    sliders,
                    variants,
                    standard
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
    }

    private static SliderMeasurement[] MeasureSliderTicks()
    {
        var fixtures = new[]
        {
            "canon-eos-6d-iso-6400.cr2",
            "fujifilm-x30.raf",
            "srgb-reference.jpg",
            "reference.heic"
        };
        var loader = new BaseLoaderRouter(
            new RawBaseLoader(),
            new StandardBaseLoader());
        var pipeline = new RenderPipeline();
        var existing = fixtures.Select(fixture =>
        {
            if (Path.GetExtension(fixture).Equals(
                    ".heic",
                    StringComparison.OrdinalIgnoreCase) &&
                MagickFormatInfo.Create(MagickFormat.Heic) is not
                    { SupportsReading: true })
            {
                return new SliderMeasurement(
                    fixture,
                    null,
                    "ImageMagick has no HEIC reader");
            }

            using var baseImage = loader.LoadPreviewBase(
                new ImageFile(Asset(fixture)),
                BaseDecodeSettings.Default,
                CancellationToken.None) ??
                throw new InvalidOperationException(
                    $"Slider fixture did not decode: {fixture}.");
            var settings = new EditSettings { Contrast = 25 };
            var elapsed = Measure(() => pipeline.Render(new RenderRequest(
                baseImage,
                settings,
                RenderIntent.Preview,
                1600,
                new RenderOptions(false, false))));
            return new SliderMeasurement(fixture, elapsed, null);
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

    private static SliderMeasurement MeasureAllChannelTicks(
        IBaseImageLoader loader,
        RenderPipeline pipeline,
        string fixture)
    {
        using var baseImage = loader.LoadPreviewBase(
            new ImageFile(Asset(fixture)),
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

    private async Task<ExportMeasurement> MeasureThreeVariants(
        OutputColorSpace target)
    {
        var file = new ImageFile(Asset("canon-eos-6d-iso-6400.cr2"));
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(_output, $"variants-{target}"),
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

    private async Task<double> MeasureStandardExport()
    {
        var file = new ImageFile(Asset("srgb-reference.jpg"));
        var settings = new ExportSettings
        {
            OutputFolder = Path.Combine(_output, "standard"),
            Format = ExportFormat.Jpeg,
            Quality = 85,
            OutputSharpening = false
        };
        var service = CreateExportService();
        return await MeasureAsync(async () =>
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

    private static async Task<double> MeasureAsync(Func<Task> operation)
    {
        await operation();
        var samples = new double[SampleCount];
        for (var index = 0; index < samples.Length; index++)
        {
            var stopwatch = Stopwatch.StartNew();
            await operation();
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

    private static string Asset(string fileName) =>
        Path.Combine(GoldenTestPaths.AssetDirectory, fileName);

    public void Dispose()
    {
        if (Directory.Exists(_output))
        {
            Directory.Delete(_output, recursive: true);
        }
    }

    private sealed record SliderMeasurement(
        string Fixture,
        double? ElapsedMs,
        string? SkipReason);

    private sealed record ExportMeasurement(
        double ElapsedMs,
        long PeakPrivateBytes);
}

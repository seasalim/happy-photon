using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ChromaNrPreviewPerformanceTests
{
    private const int SampleCount = 5;
    private const int StageSampleCount = 15;
    private readonly ITestOutputHelper _output;

    public ChromaNrPreviewPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Theory]
    [InlineData("canon-eos-6d-iso-6400.cr2")]
    [InlineData("fujifilm-x30.raf")]
    [InlineData("srgb-reference.jpg")]
    [InlineData("iphone-14-pro-iso-1000.heic")]
    public void Values50And100_ReportPairedPreviewTickCost(string fixture)
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run preview performance diagnostics.");
#if DEBUG
        Assert.Skip("Run preview performance diagnostics in Release.");
#endif
        Assert.SkipWhen(
            Path.GetExtension(fixture).Equals(".heic",
                StringComparison.OrdinalIgnoreCase) &&
            MagickFormatInfo.Create(MagickFormat.Heic) is not
                { SupportsReading: true },
            "ImageMagick has no HEIC reader.");

        var loader = new BaseLoaderRouter(
            new RawBaseLoader(),
            new StandardBaseLoader());
        using var baseImage = loader.LoadPreviewBase(
            new ImageFile(GoldenTestPaths.Asset(fixture)),
            BaseDecodeSettings.Default,
            CancellationToken.None) ??
            throw new InvalidOperationException(
                $"Preview fixture did not decode: {fixture}.");
        var pipeline = new RenderPipeline();

        foreach (var value in new[] { 50, 100 })
        {
            var pair = MeasurePair(
                () => Render(pipeline, baseImage, new EditSettings()),
                () => Render(pipeline, baseImage, new EditSettings
                {
                    Detail = new DetailSettings { ChromaNr = value }
                }));
            _output.WriteLine(
                $"{fixture} Chroma NR {value} at 1600px: neutral median " +
                $"{pair.Neutral:F2} ms, active median {pair.Active:F2} ms, " +
                $"delta {pair.Active - pair.Neutral:F2} ms over " +
                $"{SampleCount} alternating paired samples.");
            Assert.True(pair.Active - pair.Neutral <= 45,
                $"{fixture} Chroma NR {value} delta was " +
                $"{pair.Active - pair.Neutral:F2} ms; limit is 45 ms.");
            Assert.True(pair.Active <= Math.Max(150, pair.Neutral + 45),
                $"{fixture} Chroma NR {value} total was {pair.Active:F2} ms.");
        }
    }

    [Fact]
    public async Task Values50And100_ReportStageCostAtPreviewScaleShapes()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run preview performance diagnostics.");
#if DEBUG
        Assert.Skip("Run preview performance diagnostics in Release.");
#endif
        var shapes = new[]
        {
            new PreviewShape(1600, 1067, 5472, 3648),
            new PreviewShape(1600, 1200, 4032, 3024),
            new PreviewShape(1600, 1200, 1600, 1200)
        };
        foreach (var value in new[] { 50, 100 })
        foreach (var shape in shapes)
        {
            using var source = CreateImage(shape.Width, shape.Height);
            var info = CreateInfo(shape.FullWidth, shape.FullHeight);
            using (var warmup = new MagickImage(source))
            {
                RenderNoiseReduction.Apply(warmup, info,
                    new DetailSettings { ChromaNr = value });
            }

            var samples = new double[StageSampleCount];
            for (var index = 0; index < samples.Length; index++)
            {
                using var candidate = new MagickImage(source);
                var stopwatch = Stopwatch.StartNew();
                RenderNoiseReduction.Apply(candidate, info,
                    new DetailSettings { ChromaNr = value });
                stopwatch.Stop();
                samples[index] = stopwatch.Elapsed.TotalMilliseconds;
            }

            using var process = Process.GetCurrentProcess();
            var peakSamples = new long[SampleCount];
            for (var index = 0; index < peakSamples.Length; index++)
            {
                using var memoryCandidate = new MagickImage(source);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                process.Refresh();
                var baseline = process.PrivateMemorySize64;
                var peak = baseline;
                var render = Task.Run(() => RenderNoiseReduction.Apply(
                    memoryCandidate,
                    info,
                    new DetailSettings { ChromaNr = value }));
                while (!render.IsCompleted)
                {
                    await Task.Delay(1);
                    process.Refresh();
                    peak = Math.Max(peak, process.PrivateMemorySize64);
                }
                await render;
                process.Refresh();
                peak = Math.Max(peak, process.PrivateMemorySize64);
                peakSamples[index] = Math.Max(0, peak - baseline);
            }
            Array.Sort(peakSamples);
            var medianPeak = peakSamples[peakSamples.Length / 2];

            _output.WriteLine(
                $"Chroma NR {value} stage {shape.Width}x{shape.Height} from " +
                $"{shape.FullWidth}x{shape.FullHeight}: median " +
                $"{Median(samples):F2} ms over {StageSampleCount} iterations, " +
                $"median peak private delta " +
                $"{medianPeak / 1048576.0:F1} MiB over " +
                $"{SampleCount} iterations.");
        }
    }

    private static IDisposable Render(
        RenderPipeline pipeline,
        BaseImage baseImage,
        EditSettings settings) => pipeline.Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            1600,
            new RenderOptions(false, false)));

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

    private static double Median(double[] samples)
    {
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static MagickImage CreateImage(int width, int height)
    {
        var image = new MagickImage(
            MagickColors.Black,
            (uint)width,
            (uint)height)
        {
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        var channels = checked((int)pixels.Channels);
        var row = new ushort[checked(width * channels)];
        for (var y = 0; y < height; y++)
        {
            for (var index = 0; index < row.Length; index++)
            {
                var sample = checked((long)y * row.Length + index);
                var mixed = unchecked(
                    (uint)sample * 747_796_405u + 2_891_336_453u);
                row[index] = (ushort)(mixed ^ mixed >> 16);
            }
            pixels.SetArea(0, y, (uint)width, 1, row);
        }
        return image;
    }

    private static BaseImageInfo CreateInfo(int width, int height) => new(
        BaseSourceKind.Standard,
        false,
        BaseDecodeSettings.Default,
        null,
        null,
        6504,
        0,
        false,
        null,
        1,
        width,
        height);

    private sealed record PreviewShape(
        int Width,
        int Height,
        int FullWidth,
        int FullHeight);
}

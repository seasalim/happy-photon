using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderSharpeningPreviewGateTests
{
    private const int MeasurementRuns = 3;
    private const string Fixture = "canon-eos-6d-iso-6400.cr2";
    private const string ExportSharpen100Hash =
        "a9e8cb7487ffac900997edd962c2df0bad080763fd7105091c8d9e8177286ee0";

    private readonly ITestOutputHelper _output;

    public RenderSharpeningPreviewGateTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void PreviewSharpening_ProvidesFitAndZoomFeedback()
    {
        var outcome = new RawBaseLoader().LoadPreviewBaseWithOutcome(
            new ImageFile(GoldenTestPaths.Asset(Fixture)),
            BaseDecodeSettings.Default,
            CancellationToken.None);
        using var pair = outcome.Pair ?? throw new InvalidOperationException(
            $"Could not decode {Fixture}: {outcome.Failure}.");
        var interactive = pair.Interactive;
        using var fullBase = new RawBaseLoader().LoadFullBase(
            new ImageFile(GoldenTestPaths.Asset(Fixture)),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
            $"Could not fully decode {Fixture}.");
        using var large = CreateSizedBase(fullBase, 3200);

        Assert.Equal(1600u, Math.Max(interactive.Pixels.Width, interactive.Pixels.Height));
        Assert.Equal(3200u, Math.Max(large.Pixels.Width, large.Pixels.Height));

        var fitRender0 = Measure(interactive, 0, resting: false);
        var fitRender50 = Measure(interactive, 50, resting: false);
        var fitRender100 = Measure(interactive, 100, resting: false);
        var fitResting0 = Measure(interactive, 0, resting: true);
        var fitResting100 = Measure(interactive, 100, resting: true);
        var zoomRender0 = Measure(large, 0, resting: false);
        var zoomRender100 = Measure(large, 100, resting: false);
        var zoomResting0 = Measure(large, 0, resting: true);
        var zoomResting100 = Measure(large, 100, resting: true);
        var fitChangedSamples = CountChangedRgbSamples(interactive);

        foreach (var observations in new[]
        {
            fitRender0, fitRender50, fitRender100,
            fitResting0, fitResting100,
            zoomRender0, zoomRender100,
            zoomResting0, zoomResting100
        })
        {
            AssertStable(observations);
        }
        WriteEnergy("fit Render sharpen 0", fitRender0);
        WriteEnergy("fit Render sharpen 50", fitRender50);
        WriteEnergy("fit Render sharpen 100", fitRender100);
        WriteGain("fit Render", fitRender0, fitRender100);
        WriteGain("fit RenderResting", fitResting0, fitResting100);
        WriteGain("zoom Render", zoomRender0, zoomRender100);
        WriteGain("zoom RenderResting", zoomResting0, zoomResting100);
        _output.WriteLine(
            $"Fit Render sharpen 0 vs 100 changed Q16 RGB samples: {fitChangedSamples}.");

        Assert.True(fitChangedSamples > 0);
        Assert.NotEqual(fitRender0[0].Hash, fitRender100[0].Hash);
        Assert.True(MedianEnergy(fitRender0) < MedianEnergy(fitRender50));
        Assert.True(MedianEnergy(fitRender50) < MedianEnergy(fitRender100));
        Assert.True(Gain(fitResting0, fitResting100) > 1);
        Assert.True(Gain(zoomRender0, zoomRender100) > 1);
        Assert.True(Gain(zoomResting0, zoomResting100) > 1);
    }

    [Fact]
    public void ExportSharpen100_Q16RgbHashRemainsPinned()
    {
        using var baseImage = new RawBaseLoader().LoadFullBase(
            new ImageFile(GoldenTestPaths.Asset(Fixture)),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
            $"Could not decode {Fixture}.");
        var hashes = new string[MeasurementRuns];
        for (var run = 0; run < hashes.Length; run++)
        {
            using var result = new RenderPipeline().Render(CreateRequest(
                baseImage, 100, RenderIntent.Export));
            hashes[run] = HashQ16Rgb(result.Image);
        }

        _output.WriteLine(
            $"Export sharpen 100 Q16 RGB SHA-256 over {MeasurementRuns} runs: " +
            string.Join(", ", hashes));
        Assert.All(hashes, hash => Assert.Equal(hashes[0], hash));
        Assert.Equal(ExportSharpen100Hash, hashes[0]);
    }

    [Fact]
    public void PreviewCaptureSharpen100_ReportsTickCost()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run sharpening performance diagnostics.");
#if DEBUG
        Assert.Skip("Run sharpening performance diagnostics in Release.");
#endif
        PerfEnvironment.AssertFullCpu();
        var info = CreateRawInfo(5472, 3648);
        var detail = new DetailSettings { CaptureSharpen = 100 };
        using (var warmup = CreateImage(1600, 1067))
        {
            RenderSharpening.ApplyCapture(
                warmup, info, detail, RenderIntent.Preview);
        }

        var samples = new double[5];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            using var image = CreateImage(1600, 1067);
            var stopwatch = Stopwatch.StartNew();
            RenderSharpening.ApplyCapture(
                image, info, detail, RenderIntent.Preview);
            stopwatch.Stop();
            samples[sample] = stopwatch.Elapsed.TotalMilliseconds;
        }

        var median = samples.Order().ElementAt(samples.Length / 2);
        _output.WriteLine(
            $"Preview capture sharpen 100 at 1600x1067: median {median:F3} ms " +
            $"over 5 fresh images [{string.Join(", ", samples.Select(x => $"{x:F3}"))}].");
        Assert.True(median <= 15, $"Median {median:F3} ms exceeded 15 ms.");
    }

    private RenderObservation[] Measure(BaseImage baseImage, int sharpen, bool resting)
    {
        var observations = new RenderObservation[MeasurementRuns];
        for (var run = 0; run < observations.Length; run++)
        {
            var request = CreateRequest(baseImage, sharpen, RenderIntent.Preview);
            using var result = resting
                ? new RenderPipeline().RenderResting(
                    request,
                    RenderExecutionOptions.Resting(CancellationToken.None))
                : new RenderPipeline().Render(request);
            observations[run] = new RenderObservation(
                HighPassLumaEnergy(result.Image),
                HashQ16Rgb(result.Image));
        }
        return observations;
    }

    private static RenderRequest CreateRequest(
        BaseImage baseImage,
        int sharpen,
        RenderIntent intent) =>
        new(
            baseImage,
            new EditSettings
            {
                Detail = new DetailSettings { CaptureSharpen = sharpen }
            },
            intent,
            MaxDimension: null,
            new RenderOptions(false, false),
            OutputColorSpace.Srgb,
            OutputSharpeningMode.Off);

    private static double HighPassLumaEnergy(MagickImage image)
    {
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);
        var rgb = RenderPipelineTestSupport.ReadPixels(image);
        var luma = new float[checked(width * height)];
        for (var pixel = 0; pixel < luma.Length; pixel++)
        {
            var rgbIndex = pixel * 3;
            luma[pixel] = (float)((0.2126 * rgb[rgbIndex] +
                0.7152 * rgb[rgbIndex + 1] +
                0.0722 * rgb[rgbIndex + 2]) / ushort.MaxValue);
        }

        double sumSquared = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double blurred = 0;
                for (var offsetY = -1; offsetY <= 1; offsetY++)
                {
                    var row = Math.Clamp(y + offsetY, 0, height - 1) * width;
                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        blurred += luma[row + Math.Clamp(x + offsetX, 0, width - 1)];
                    }
                }
                var difference = luma[y * width + x] - blurred / 9;
                sumSquared += difference * difference;
            }
        }
        return sumSquared / luma.Length;
    }

    private static string HashQ16Rgb(MagickImage image)
    {
        var rgb = RenderPipelineTestSupport.ReadPixels(image);
        var bytes = MemoryMarshal.AsBytes(rgb.AsSpan());
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static int CountChangedRgbSamples(BaseImage baseImage)
    {
        using var zero = new RenderPipeline().Render(CreateRequest(
            baseImage, 0, RenderIntent.Preview));
        using var hundred = new RenderPipeline().Render(CreateRequest(
            baseImage, 100, RenderIntent.Preview));
        var zeroPixels = RenderPipelineTestSupport.ReadPixels(zero.Image);
        var hundredPixels = RenderPipelineTestSupport.ReadPixels(hundred.Image);
        Assert.Equal(zeroPixels.Length, hundredPixels.Length);
        var changed = 0;
        for (var index = 0; index < zeroPixels.Length; index++)
        {
            if (zeroPixels[index] != hundredPixels[index]) changed++;
        }
        return changed;
    }

    private static void AssertStable(RenderObservation[] observations) =>
        Assert.All(observations, value => Assert.Equal(observations[0], value));

    private static double MedianEnergy(RenderObservation[] observations) =>
        observations.Select(value => value.Energy).Order().ElementAt(
            observations.Length / 2);

    private static double Gain(
        RenderObservation[] zero,
        RenderObservation[] hundred) =>
        MedianEnergy(hundred) / MedianEnergy(zero);

    private void WriteEnergy(string label, RenderObservation[] observations) =>
        _output.WriteLine(
            $"{label}: median energy {MedianEnergy(observations):G17} " +
            $"over {observations.Length} runs " +
            $"[{string.Join(", ", observations.Select(x => x.Energy.ToString("G17")))}].");

    private void WriteGain(
        string label,
        RenderObservation[] zero,
        RenderObservation[] hundred) =>
        _output.WriteLine(
            $"{label}: E(100)/E(0) = " +
            $"{Gain(zero, hundred):G17}.");

    private static MagickImage CreateImage(int width, int height)
    {
        var image = new MagickImage(MagickColors.Black, (uint)width, (uint)height)
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
                var mixed = unchecked((uint)sample * 747_796_405u + 2_891_336_453u);
                row[index] = (ushort)(mixed ^ mixed >> 16);
            }
            pixels.SetArea(0, y, (uint)width, 1, row);
        }
        return image;
    }

    private static BaseImage CreateSizedBase(BaseImage source, int longEdge)
    {
        var pixels = new MagickImage(source.Pixels);
        try
        {
            BitmapConversionService.ResizeToMaxDimension(pixels, longEdge);
            var result = new BaseImage(pixels, source.Info);
            pixels = null!;
            return result;
        }
        finally
        {
            pixels?.Dispose();
        }
    }

    private static BaseImageInfo CreateRawInfo(int width, int height) =>
        new(
            BaseSourceKind.RawLibRaw,
            true,
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

    private sealed record RenderObservation(double Energy, string Hash);
}

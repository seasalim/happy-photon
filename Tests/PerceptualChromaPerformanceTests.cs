using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PerceptualChromaPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public PerceptualChromaPerformanceTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void ProjectionHeavyPass_MeetsAgxCrossingCostClass()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to run perceptual-chroma performance gates.");
#if DEBUG
        Assert.Skip("Run perceptual-chroma performance gates in Release.");
#endif
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "canon-eos-6d-iso-6400.cr2");
        using var baseImage = new RawBaseLoader().LoadPreviewBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                "The chroma performance fixture did not decode.");
        using var upstream = new RenderPipeline().RenderDisplayRec2020(
            new RenderRequest(
                baseImage,
                new EditSettings(),
                RenderIntent.Preview,
                null,
                new RenderOptions(false, false)));
        var values = RenderPipelineTestSupport.ReadPixels(upstream);
        var projected = CountProjected(values, saturation: 100);
        Assert.True(projected > values.Length / 3 / 100,
            $"Projection-heavy fixture had only {projected} projected pixels.");

        using var working = new MagickImage(upstream);
        var chromaMedian = MeasureChroma(working, values);
        var crossingMedian = MeasureCrossing(values);
        _output.WriteLine(
            $"OKLCh S=+100 pass including pixel-cache traffic: " +
            $"{chromaMedian:F1} ms; same-fixture AgX array comparator: " +
            $"{crossingMedian:F1} ms; projected pixels: {projected}.");

        Assert.True(chromaMedian <= 60,
            $"Projection-heavy chroma pass took {chromaMedian:F1} ms; " +
            "AgX crossing cost-class budget is 60 ms.");
    }

    private static double MeasureChroma(
        MagickImage image,
        ushort[] source)
    {
        var settings = new EditSettings { Saturation = 100 };
        Reset(image, source);
        RenderChromaStage.Apply(image, settings);
        var samples = new double[5];
        for (var iteration = 0; iteration < samples.Length; iteration++)
        {
            Reset(image, source);
            var stopwatch = Stopwatch.StartNew();
            RenderChromaStage.Apply(image, settings);
            stopwatch.Stop();
            samples[iteration] = stopwatch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static double MeasureCrossing(ushort[] source)
    {
        var crossing = new AgxCrossing(
            AgxToneEnginePropertyTests.Parameters(contrast: 25));
        var destination = new ushort[source.Length];
        crossing.Apply(source, destination);
        var samples = new double[5];
        for (var iteration = 0; iteration < samples.Length; iteration++)
        {
            var stopwatch = Stopwatch.StartNew();
            crossing.Apply(source, destination);
            stopwatch.Stop();
            samples[iteration] = stopwatch.Elapsed.TotalMilliseconds;
        }
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private static int CountProjected(ushort[] values, int saturation)
    {
        var result = 0;
        for (var offset = 0; offset < values.Length; offset += 3)
        {
            if (values[offset] == values[offset + 1] &&
                values[offset + 1] == values[offset + 2])
            {
                continue;
            }
            var source = OklabColor.FromEncodedRec2020(new OklabRgb(
                values[offset] / (double)ushort.MaxValue,
                values[offset + 1] / (double)ushort.MaxValue,
                values[offset + 2] / (double)ushort.MaxValue));
            var target = OklabColor.ApplyChroma(source, saturation, 0);
            result += OklabColor.IsInGamut(
                OklabColor.ToLinearRec2020(target)) ? 0 : 1;
        }
        return result;
    }

    private static void Reset(MagickImage image, ushort[] values)
    {
        using var pixels = image.GetPixels();
        pixels.SetArea(0, 0, image.Width, image.Height, values);
    }
}

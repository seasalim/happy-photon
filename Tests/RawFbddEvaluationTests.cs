using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using Xunit.Abstractions;
using static HappyPhoton.Tests.RawBaseLoaderTestSupport;

namespace HappyPhoton.Tests;

public sealed class RawFbddEvaluationTests
{
    private const int CropSize = 512;
    private readonly ITestOutputHelper _output;

    public RawFbddEvaluationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void HighIsoRaw_MeasuresOffLightAndFull()
    {
        Skip.If(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_FBDD_EVAL") != "1",
            "Set HAPPY_PHOTON_FBDD_EVAL=1 to run three full high-ISO RAW decodes.");
        var loader = new RawBaseLoader();
        var file = new ImageFile(Asset("canon-eos-6d-iso-6400.cr2"));
        var off = Measure(loader, file, FbddMode.Off);
        var light = Measure(loader, file, FbddMode.Light);
        var full = Measure(loader, file, FbddMode.Full);
        var lightDelta = RmsDelta(off.Samples, light.Samples);
        var fullDelta = RmsDelta(off.Samples, full.Samples);

        Assert.True(lightDelta > 0, "FBDD Light must change full-decode pixels.");
        Assert.True(fullDelta > 0, "FBDD Full must change full-decode pixels.");
        Assert.True(
            light.ChromaVariation < off.ChromaVariation,
            "FBDD Light must reduce center-crop chroma variation.");
        Assert.True(
            full.ChromaVariation < light.ChromaVariation,
            "FBDD Full must reduce more chroma variation than Light.");
        WriteMeasurement(off);
        WriteMeasurement(light);
        WriteMeasurement(full);
        _output.WriteLine(
            $"Center-crop RMS delta from Off: " +
            $"Light={lightDelta:F2}, Full={fullDelta:F2} Q16.");
        _output.WriteLine(
            $"Center-crop chroma-variation reduction from Off: " +
            $"Light={Reduction(off, light):P1}, " +
            $"Full={Reduction(off, full):P1}.");
    }

    private static FbddMeasurement Measure(
        RawBaseLoader loader,
        ImageFile file,
        FbddMode mode)
    {
        var stopwatch = Stopwatch.StartNew();
        using var image = loader.LoadFullBase(
            file,
            new BaseDecodeSettings(
                HlReconstructionMode.Blend,
                mode),
            CancellationToken.None);
        stopwatch.Stop();
        Assert.NotNull(image);
        var samples = ReadCenterCrop(image!.Pixels);
        return new FbddMeasurement(
            mode,
            stopwatch.Elapsed,
            samples,
            ChromaVariation(samples, CropSize, CropSize));
    }

    private void WriteMeasurement(FbddMeasurement measurement)
    {
        _output.WriteLine(
            $"{measurement.Mode}: " +
            $"{measurement.Elapsed.TotalSeconds:F2} s, " +
            $"center-crop chroma variation " +
            $"{measurement.ChromaVariation:F2} Q16.");
    }

    private static double Reduction(
        FbddMeasurement baseline,
        FbddMeasurement candidate) =>
        1 - candidate.ChromaVariation / baseline.ChromaVariation;

    private static ushort[] ReadCenterCrop(MagickImage image)
    {
        Assert.True(image.Width >= CropSize && image.Height >= CropSize);
        var x = (checked((int)image.Width) - CropSize) / 2;
        var y = (checked((int)image.Height) - CropSize) / 2;
        using var pixels = image.GetPixels();
        var samples = pixels.GetArea(x, y, CropSize, CropSize);
        Assert.NotNull(samples);
        return samples!;
    }

    private static double RmsDelta(ushort[] first, ushort[] second)
    {
        Assert.Equal(first.Length, second.Length);
        double sum = 0;
        for (var index = 0; index < first.Length; index++)
        {
            var delta = first[index] - second[index];
            sum += (double)delta * delta;
        }
        return Math.Sqrt(sum / first.Length);
    }

    private static double ChromaVariation(
        ushort[] samples,
        int width,
        int height)
    {
        double sum = 0;
        var comparisons = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 3;
                if (x > 0)
                {
                    sum += ChromaDeltaSquared(samples, index, index - 3);
                    comparisons++;
                }
                if (y > 0)
                {
                    sum += ChromaDeltaSquared(
                        samples,
                        index,
                        index - width * 3);
                    comparisons++;
                }
            }
        }
        return Math.Sqrt(sum / (comparisons * 2));
    }

    private static double ChromaDeltaSquared(
        ushort[] samples,
        int first,
        int second)
    {
        var firstLuma = Luma(samples, first);
        var secondLuma = Luma(samples, second);
        var cbDelta = (samples[first + 2] - firstLuma) -
            (samples[second + 2] - secondLuma);
        var crDelta = (samples[first] - firstLuma) -
            (samples[second] - secondLuma);
        return cbDelta * cbDelta + crDelta * crDelta;
    }

    private static double Luma(ushort[] samples, int index) =>
        0.2126 * samples[index] +
        0.7152 * samples[index + 1] +
        0.0722 * samples[index + 2];

    private sealed record FbddMeasurement(
        FbddMode Mode,
        TimeSpan Elapsed,
        ushort[] Samples,
        double ChromaVariation);
}

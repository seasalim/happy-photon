using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ChromaNoiseReductionQualityTests
{
    private const int SampleCount = 5;
    private const double LegacySeededResidual = 809.739110;
    private const double LegacyBlotchResidual = 1485.025271;
    private const double LegacyEdgeRetention = 0.333338846;
    private readonly ITestOutputHelper _output;

    public ChromaNoiseReductionQualityTests(ITestOutputHelper output) =>
        _output = output;

    [Fact]
    public void Wavelet50_BeatsPinnedBoxBlurQualityReferences()
    {
        using var cleanNoise = CreateCleanImage(192, 128);
        using var seededNoise = AddSeededChromaNoise(cleanNoise, 225);
        using var cleanBlotch = CreateCleanImage(192, 128);
        using var blotches = AddLowFrequencyBlotches(cleanBlotch);
        using var verticalEdge = CreateChromaEdge(
            192, 128, EdgeOrientation.Vertical);
        using var horizontalEdge = CreateChromaEdge(
            192, 128, EdgeOrientation.Horizontal);
        using var gamutBoundary = CreateGamutBoundary(192, 128);
        var boundaryBefore = RenderPipelineTestSupport.ReadPixels(gamutBoundary);

        var noiseSamples = MeasureResidual(cleanNoise, seededNoise);
        var blotchSamples = MeasureResidual(cleanBlotch, blotches);
        var verticalEdgeSamples = MeasureEdgeRetention(
            verticalEdge, EdgeOrientation.Vertical);
        var horizontalEdgeSamples = MeasureEdgeRetention(
            horizontalEdge, EdgeOrientation.Horizontal);
        ApplyWavelet(gamutBoundary);
        var boundaryAfter = RenderPipelineTestSupport.ReadPixels(gamutBoundary);

        var noise = Median(noiseSamples);
        var blotch = Median(blotchSamples);
        var verticalEdgeRetention = Median(verticalEdgeSamples);
        var horizontalEdgeRetention = Median(horizontalEdgeSamples);
        _output.WriteLine(
            $"Wavelet Chroma NR 50 seeded residual: {noise:F6} Q16 RMSE " +
            $"(median of {SampleCount}).");
        _output.WriteLine(
            $"Wavelet Chroma NR 50 blotch residual: {blotch:F6} Q16 RMSE " +
            $"(median of {SampleCount}).");
        _output.WriteLine(
            $"Wavelet Chroma NR 50 vertical-edge retention: " +
            $"{verticalEdgeRetention:F9} ratio " +
            $"({verticalEdgeRetention:P6}) " +
            $"(median of {SampleCount}).");
        _output.WriteLine(
            $"Wavelet Chroma NR 50 horizontal-edge retention: " +
            $"{horizontalEdgeRetention:F9} ratio " +
            $"({horizontalEdgeRetention:P6}) " +
            $"(median of {SampleCount}).");

        Assert.All(noiseSamples, value => Assert.Equal(noise, value));
        Assert.All(blotchSamples, value => Assert.Equal(blotch, value));
        Assert.All(verticalEdgeSamples,
            value => Assert.Equal(verticalEdgeRetention, value));
        Assert.All(horizontalEdgeSamples,
            value => Assert.Equal(horizontalEdgeRetention, value));
        Assert.True(noise < LegacySeededResidual,
            $"Seeded residual {noise:F6} did not beat {LegacySeededResidual:F6}.");
        Assert.True(blotch < LegacyBlotchResidual,
            $"Blotch residual {blotch:F6} did not beat {LegacyBlotchResidual:F6}.");
        Assert.True(verticalEdgeRetention >= LegacyEdgeRetention,
            $"Vertical-edge retention {verticalEdgeRetention:F9} fell below " +
            $"{LegacyEdgeRetention:F9}.");
        Assert.True(horizontalEdgeRetention >= LegacyEdgeRetention,
            $"Horizontal-edge retention {horizontalEdgeRetention:F9} fell below " +
            $"{LegacyEdgeRetention:F9}.");
        AssertQuantizedLumaUnchanged(boundaryBefore, boundaryAfter);
    }

    private static double[] MeasureResidual(
        MagickImage clean,
        MagickImage degraded)
    {
        var cleanPixels = RenderPipelineTestSupport.ReadPixels(clean);
        var samples = new double[SampleCount];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            using var candidate = new MagickImage(degraded);
            ApplyWavelet(candidate);
            samples[sample] = ChromaResidualRmse(
                cleanPixels,
                RenderPipelineTestSupport.ReadPixels(candidate));
        }
        return samples;
    }

    private static double[] MeasureEdgeRetention(
        MagickImage clean,
        EdgeOrientation orientation)
    {
        var width = checked((int)clean.Width);
        var height = checked((int)clean.Height);
        var baseline = EdgeContrast(
            RenderPipelineTestSupport.ReadPixels(clean),
            width,
            height,
            orientation);
        var samples = new double[SampleCount];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            using var candidate = new MagickImage(clean);
            ApplyWavelet(candidate);
            samples[sample] = EdgeContrast(
                RenderPipelineTestSupport.ReadPixels(candidate),
                width,
                height,
                orientation) / baseline;
        }
        return samples;
    }

    private static void ApplyWavelet(MagickImage image) =>
        RenderNoiseReduction.Apply(
        image,
        CreateInfo(checked((int)image.Width), checked((int)image.Height)),
        new DetailSettings { ChromaNr = 50 });

    private static void AssertQuantizedLumaUnchanged(
        ushort[] before,
        ushort[] after)
    {
        Assert.Equal(before.Length, after.Length);
        for (var index = 0; index < before.Length; index += 3)
        {
            Assert.Equal(
                ToQuantum(ReadLuma(before, index)),
                ToQuantum(ReadLuma(after, index)));
        }
    }

    // RMSE over both Rec.2020 chroma coordinates: Cb=B-Y and Cr=R-Y.
    private static double ChromaResidualRmse(
        ushort[] clean,
        ushort[] candidate)
    {
        double squaredError = 0;
        for (var index = 0; index < clean.Length; index += 3)
        {
            var (cleanCb, cleanCr) = ReadChroma(clean, index);
            var (candidateCb, candidateCr) = ReadChroma(candidate, index);
            squaredError += Square(candidateCb - cleanCb) +
                Square(candidateCr - cleanCr);
        }
        return Math.Sqrt(squaredError / (clean.Length / 3 * 2));
    }

    // Vector contrast between the adjacent rows or columns straddling the edge.
    private static double EdgeContrast(
        ushort[] pixels,
        int width,
        int height,
        EdgeOrientation orientation)
    {
        var firstSide = orientation == EdgeOrientation.Vertical
            ? width / 2 - 1
            : height / 2 - 1;
        var secondSide = firstSide + 1;
        var sampleCount = orientation == EdgeOrientation.Vertical
            ? height
            : width;
        double firstCb = 0;
        double firstCr = 0;
        double secondCb = 0;
        double secondCr = 0;
        for (var sample = 0; sample < sampleCount; sample++)
        {
            var first = orientation == EdgeOrientation.Vertical
                ? (sample * width + firstSide) * 3
                : (firstSide * width + sample) * 3;
            var second = orientation == EdgeOrientation.Vertical
                ? (sample * width + secondSide) * 3
                : (secondSide * width + sample) * 3;
            var firstChroma = ReadChroma(pixels, first);
            var secondChroma = ReadChroma(pixels, second);
            firstCb += firstChroma.Cb;
            firstCr += firstChroma.Cr;
            secondCb += secondChroma.Cb;
            secondCr += secondChroma.Cr;
        }
        var cb = (secondCb - firstCb) / sampleCount;
        var cr = (secondCr - firstCr) / sampleCount;
        return Math.Sqrt(cb * cb + cr * cr);
    }

    private static MagickImage CreateCleanImage(int width, int height) =>
        CreateImage(width, height, (x, y) =>
        {
            var luma = 25_000 + 12_000.0 * x / (width - 1) +
                4_000.0 * y / (height - 1);
            var cb = -1_500 + 3_000.0 * y / (height - 1);
            var cr = 1_000 - 2_000.0 * x / (width - 1);
            return (luma, cb, cr);
        });

    private static MagickImage AddSeededChromaNoise(
        MagickImage clean,
        int seed)
    {
        var random = new Random(seed);
        var width = checked((int)clean.Width);
        var height = checked((int)clean.Height);
        var source = RenderPipelineTestSupport.ReadPixels(clean);
        return CreateImage(width, height, (x, y) =>
        {
            var index = (y * width + x) * 3;
            var luma = ReadLuma(source, index);
            var (cb, cr) = ReadChroma(source, index);
            return (
                luma,
                cb + NextGaussian(random) * 2_400,
                cr + NextGaussian(random) * 2_400);
        });
    }

    private static MagickImage AddLowFrequencyBlotches(MagickImage clean)
    {
        var width = checked((int)clean.Width);
        var height = checked((int)clean.Height);
        var source = RenderPipelineTestSupport.ReadPixels(clean);
        return CreateImage(width, height, (x, y) =>
        {
            var index = (y * width + x) * 3;
            var luma = ReadLuma(source, index);
            var (cb, cr) = ReadChroma(source, index);
            var cbBlotch = 3_200 * Math.Sin(2 * Math.PI * x / 64) *
                Math.Sin(2 * Math.PI * y / 48);
            var crBlotch = 2_800 * Math.Cos(2 * Math.PI * x / 72) *
                Math.Cos(2 * Math.PI * y / 56);
            return (luma, cb + cbBlotch, cr + crBlotch);
        });
    }

    private static MagickImage CreateChromaEdge(
        int width,
        int height,
        EdgeOrientation orientation) =>
        CreateImage(width, height, (x, y) =>
            (orientation == EdgeOrientation.Vertical
                ? x < width / 2
                : y < height / 2)
            ? (32_000, -5_000, 4_000)
            : (32_000, 5_000, -4_000));

    private static MagickImage CreateGamutBoundary(int width, int height) =>
        CreateImage(width, height, (x, y) =>
        {
            var bright = y >= height / 2;
            var luma = bright ? 58_000 : 7_500;
            var sign = ((x / 8 + y / 8) & 1) == 0 ? 1 : -1;
            var cb = sign * (bright ? 7_000 : 6_500);
            var cr = -sign * (bright ? 6_000 : 7_000);
            return (luma, cb, cr);
        });

    private static MagickImage CreateImage(
        int width,
        int height,
        Func<int, int, (double Y, double Cb, double Cr)> sample)
    {
        var values = new ushort[checked(width * height * 3)];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var (luma, cb, cr) = sample(x, y);
            var r = luma + cr;
            var b = luma + cb;
            var g = (luma - Rec2020Luminance.Red * r -
                Rec2020Luminance.Blue * b) / Rec2020Luminance.Green;
            var index = (y * width + x) * 3;
            values[index] = ToQuantum(r);
            values[index + 1] = ToQuantum(g);
            values[index + 2] = ToQuantum(b);
        }

        var image = new MagickImage(MagickColors.Black, (uint)width, (uint)height)
        {
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        pixels.SetArea(0, 0, (uint)width, (uint)height, values);
        return image;
    }

    private static (double Cb, double Cr) ReadChroma(
        ushort[] pixels,
        int index)
    {
        var luma = ReadLuma(pixels, index);
        return (pixels[index + 2] - luma, pixels[index] - luma);
    }

    private static double ReadLuma(ushort[] pixels, int index) =>
        Rec2020Luminance.Red * pixels[index] +
        Rec2020Luminance.Green * pixels[index + 1] +
        Rec2020Luminance.Blue * pixels[index + 2];

    private static double NextGaussian(Random random)
    {
        var u1 = 1 - random.NextDouble();
        var u2 = 1 - random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    private static ushort ToQuantum(double value) =>
        (ushort)Math.Clamp(Math.Round(value), 0, ushort.MaxValue);

    private static double Square(double value) => value * value;

    private static double Median(double[] samples)
    {
        Array.Sort(samples);
        return samples[samples.Length / 2];
    }

    private enum EdgeOrientation
    {
        Vertical,
        Horizontal
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
}

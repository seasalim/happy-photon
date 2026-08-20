using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ToneLutApplicatorTests
{

    [Theory]
    [InlineData(0.0, 0, 0, 0)]
    [InlineData(1.25, 15, 30, -50)]
    [InlineData(-1.5, -20, -40, 80)]
    public void Apply_MatchesAnalyticDoubleNodes(
        double exposure,
        int brightness,
        int contrast,
        int highlights)
    {
        var parameters = new ToneParams(
            exposure,
            1,
            brightness,
            contrast,
            20,
            highlights,
            false,
            new CurveData());
        var lut = ToneLut.Compose(parameters);
        using var source = CreateAllQuantumValues();
        var sourceSamples = RenderPipelineTestSupport.ReadPixels(source);
        using var actual = (MagickImage)source.Clone();
        ToneLutApplicator.Apply(actual, lut);
        var expected = sourceSamples
            .Select(sample => ToQuantum(ToneLut.Evaluate(
                parameters,
                sample / (double)ushort.MaxValue)))
            .ToArray();

        Assert.Equal(
            expected,
            RenderPipelineTestSupport.ReadPixels(actual));
    }

    [Fact]
    public void Apply_PreservesAlpha()
    {
        ushort[] samples = [1000, 2000, 3000, 12345];
        using var image = new MagickImage(MagickColors.Transparent, 1, 1);
        image.ImportPixels(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            new PixelImportSettings(
                1,
                1,
                StorageType.Short,
                PixelMapping.RGBA));

        ToneLutApplicator.Apply(
            image,
            ToneLut.Compose(new ToneParams(
                1,
                1,
                0,
                0,
                0,
                0,
                false,
                new CurveData())));

        var actual = image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGBA) ??
            throw new InvalidOperationException("Unable to read RGBA pixels.");
        Assert.Equal(samples[3], actual[3]);
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public void MatrixApply_InterpolatesOnUnroundedDoubleMatrixOutput(
        string caseName,
        double[,] matrix)
    {
        Assert.False(string.IsNullOrWhiteSpace(caseName));
        var lut = ToneLut.Compose(new ToneParams(
            0.75, 1.3, 10, -20, 30, -40, true, new CurveData()));
        var random = new Random(1729);
        var samples = new ushort[4096 * 4];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = checked((ushort)random.Next(ushort.MaxValue + 1));
        }
        using var actual = CreateRgba(samples);
        ToneLutApplicator.Apply(actual, matrix, lut);

        var expectedSamples = (ushort[])samples.Clone();
        for (var offset = 0; offset < expectedSamples.Length; offset += 4)
        {
            var red = samples[offset] / (double)ushort.MaxValue;
            var green = samples[offset + 1] / (double)ushort.MaxValue;
            var blue = samples[offset + 2] / (double)ushort.MaxValue;
            expectedSamples[offset] = Transform(0);
            expectedSamples[offset + 1] = Transform(1);
            expectedSamples[offset + 2] = Transform(2);

            ushort Transform(int row) => ToQuantum(
                ToneLutApplicator.Interpolate(
                    lut,
                    matrix[row, 0] * red +
                    matrix[row, 1] * green +
                    matrix[row, 2] * blue));
        }
        var actualSamples = actual.GetPixelsUnsafe()
            .ToShortArray(PixelMapping.RGBA);
        Assert.NotNull(actualSamples);
        Assert.Equal(expectedSamples, actualSamples);
    }

    public static TheoryData<string, double[,]> MatrixCases => new()
    {
        {
            "synthetic",
            new double[,]
            {
                { 0.75, -0.10, 0.05 },
                { 0.01, 0.85, 0.02 },
                { -0.03, 0.10, 0.60 }
            }
        },
        {
            "production working-to-display",
            ChromaticAdaptation.NormalizeForRender(
                RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb).Matrix
        }
    };

    private static MagickImage CreateAllQuantumValues()
    {
        var samples = new ushort[(ushort.MaxValue + 1) * 3];
        for (var sample = 0; sample <= ushort.MaxValue; sample++)
        {
            var offset = sample * 3;
            samples[offset] = (ushort)sample;
            samples[offset + 1] = (ushort)(ushort.MaxValue - sample);
            samples[offset + 2] = (ushort)sample;
        }

        return RawBaseLoader.ImportRgb16(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            ushort.MaxValue + 1,
            1);
    }

    private static MagickImage CreateRgba(ushort[] samples)
    {
        var image = new MagickImage(MagickColors.Transparent, 4096, 1);
        image.ImportPixels(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            new PixelImportSettings(
                4096,
                1,
                StorageType.Short,
                PixelMapping.RGBA));
        return image;
    }

    private static ushort ToQuantum(double value) =>
        (ushort)Math.Round(
            Math.Clamp(value, 0, 1) * ushort.MaxValue,
            MidpointRounding.AwayFromZero);
}

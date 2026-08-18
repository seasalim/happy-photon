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
    public void Apply_IsBitIdenticalToPinnedMagickClut(
        double exposure,
        int brightness,
        int contrast,
        int highlights)
    {
        var lut = ToneLut.Compose(new ToneParams(
            exposure,
            1,
            brightness,
            contrast,
            20,
            highlights,
            false,
            new CurveData()));
        using var expected = CreateAllQuantumValues();
        using var actual = (MagickImage)expected.Clone();

        RenderColorEncoding.ApplyLut(expected, lut);
        ToneLutApplicator.Apply(actual, lut);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(expected),
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
    public void MatrixApply_MatchesLegacyMagickMatrixThenClutWithinPlatformTolerance(
        string caseName,
        double[,] matrix)
    {
        var lut = ToneLut.Compose(new ToneParams(
            0.75, 1.3, 10, -20, 30, -40, true, new CurveData()));
        var random = new Random(1729);
        var samples = new ushort[4096 * 4];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = checked((ushort)random.Next(ushort.MaxValue + 1));
        }
        using var expected = CreateRgba(samples);
        using var actual = (MagickImage)expected.Clone();

        expected.ColorMatrix(new MagickColorMatrix(3,
        [
            matrix[0, 0], matrix[0, 1], matrix[0, 2],
            matrix[1, 0], matrix[1, 1], matrix[1, 2],
            matrix[2, 0], matrix[2, 1], matrix[2, 2]
        ]));
        ToneLutApplicator.Apply(expected, lut);
        ToneLutApplicator.Apply(actual, matrix, lut);

        var expectedSamples = expected.GetPixelsUnsafe()
            .ToShortArray(PixelMapping.RGBA);
        var actualSamples = actual.GetPixelsUnsafe()
            .ToShortArray(PixelMapping.RGBA);
        Assert.NotNull(expectedSamples);
        Assert.NotNull(actualSamples);

        Assert.Equal(expectedSamples!.Length, actualSamples!.Length);
        var worstDifference = 0;
        var worstIndex = 0;
        for (var index = 0; index < expectedSamples.Length; index++)
        {
            var difference = Math.Abs(expectedSamples[index] - actualSamples[index]);
            if (difference > worstDifference)
            {
                worstDifference = difference;
                worstIndex = index;
            }
        }

        // The passes differ only at the matrix stage, where both round half-up and clamp,
        // so they can disagree by at most one Q16 code; the LUT then amplifies that by its
        // steepest per-code step. Deriving the bound from the LUT keeps it correct if the
        // tone parameters above ever change. x64 carries the regression proof: the fused
        // pass is architecture-invariant managed code, so a real defect in it shows up
        // there as inequality rather than hiding inside this tolerance.
        var tolerance = RuntimeInformation.ProcessArchitecture is Architecture.X64
            ? 0
            : MaxAdjacentInterpolationStep(lut);

        Assert.True(
            worstDifference <= tolerance,
            $"{caseName} differed from the legacy pass by {worstDifference} Q16 codes " +
            $"({worstDifference / 257.0:F3} of an 8-bit code) at sample {worstIndex}, " +
            $"above the {tolerance}-code tolerance.");
    }

    private static int MaxAdjacentInterpolationStep(ushort[] lut)
    {
        var worst = 0;
        for (var value = 0; value < ushort.MaxValue; value++)
        {
            worst = Math.Max(worst, Math.Abs(
                ToneLutApplicator.Interpolate(lut, (ushort)(value + 1)) -
                ToneLutApplicator.Interpolate(lut, (ushort)value)));
        }
        return worst;
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
}

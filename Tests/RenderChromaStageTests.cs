using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderChromaStageTests
{
    [Fact]
    public void IdentitySkip_IsBitExactAndDoesNotTouchDisposedPixels()
    {
        using var image = CreateRgbaImage(
            [1000, 2000, 3000, 4000, 30000, 20000, 10000, 50000]);
        var before = ReadRgba(image);

        Assert.False(RenderChromaStage.Apply(image, new EditSettings()));
        Assert.Equal(before, ReadRgba(image));

        var disposed = CreateRgbaImage([1000, 2000, 3000, 4000]);
        disposed.Dispose();
        Assert.False(RenderChromaStage.Apply(disposed, new EditSettings()));
    }

    [Theory]
    [InlineData(-100, -100)]
    [InlineData(-50, 100)]
    [InlineData(50, -100)]
    [InlineData(100, 100)]
    public void ActiveChroma_AchromaticCodesAndAlphaAreBitExact(
        int saturation,
        int vibrance)
    {
        ushort[] values =
        [
            0, 0, 0, 1000,
            117, 117, 117, 2000,
            32768, 32768, 32768, 3000,
            65535, 65535, 65535, 4000
        ];
        using var image = CreateRgbaImage(values);

        Assert.True(RenderChromaStage.Apply(
            image,
            new EditSettings
            {
                Saturation = saturation,
                Vibrance = vibrance
            }));

        Assert.Equal(values, ReadRgba(image));
    }

    [Fact]
    public void SaturationMinus100_WritesExactGrayscaleAndPreservesAlpha()
    {
        using var image = CreateRgbaImage(
        [
            1200, 22000, 61000, 12345,
            60000, 12000, 24000, 54321
        ]);

        RenderChromaStage.Apply(
            image,
            new EditSettings { Saturation = -100, Vibrance = 100 });
        var actual = ReadRgba(image);

        Assert.Equal(actual[0], actual[1]);
        Assert.Equal(actual[1], actual[2]);
        Assert.Equal((ushort)12345, actual[3]);
        Assert.Equal(actual[4], actual[5]);
        Assert.Equal(actual[5], actual[6]);
        Assert.Equal((ushort)54321, actual[7]);
    }

    [Fact]
    public void BoundedBands_AreBitIdenticalToSingleBand()
    {
        const int width = 29;
        const int height = 17;
        var random = new Random(2162);
        var values = new ushort[width * height * 4];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = checked((ushort)random.Next(ushort.MaxValue + 1));
        }
        using var single = CreateRgbaImage(values, width);
        using var bands = CreateRgbaImage(values, width);
        var settings = new EditSettings { Saturation = 87, Vibrance = 63 };

        RenderChromaStage.Apply(single, settings, int.MaxValue);
        RenderChromaStage.Apply(bands, settings, width * 3);

        Assert.Equal(ReadRgba(single), ReadRgba(bands));
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void ActiveRgba_OrdinaryAndRestingPreserveAlphaAndMatch(
        bool isRaw,
        int workerCap)
    {
        using var baseImage = CreateRgbaBase(isRaw);
        var settings = new EditSettings
        {
            Saturation = 41,
            Vibrance = -52,
            Detail = new DetailSettings { CaptureSharpen = 0 }
        };
        var request = new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            null,
            new RenderOptions(false, false));
        var pipeline = new RenderPipeline();

        using var ordinary = pipeline.Render(request);
        using var resting = pipeline.RenderResting(
            request,
            RenderExecutionOptions.Resting(
                CancellationToken.None,
                workerCap));
        var ordinaryRgba = ReadRgba(ordinary.Image);
        var restingRgba = ReadRgba(resting.Image);

        Assert.Equal(ordinaryRgba, restingRgba);
        Assert.Equal((ushort)12345, ordinaryRgba[3]);
        Assert.Equal((ushort)54321, ordinaryRgba[7]);
    }

    [Fact]
    public void ProductionPath_HasOneFinalQ16RoundingBoundary()
    {
        ushort[][] inputs =
        [
            [1234, 23456, 54321],
            [49151, 19001, 9123],
            [60000, 9000, 3000],
            [3211, 4217, 5179],
            [50000, 12000, 52000]
        ];
        var settings = new EditSettings { Saturation = 37, Vibrance = -43 };
        var distinguished = 0;
        foreach (var input in inputs)
        {
            using var image = CreateRgbImage(input);
            RenderChromaStage.Apply(image, settings);
            var actual = RenderPipelineTestSupport.ReadPixels(image);
            var reference = ReferenceQ16(input, settings, quantizeLinear: false);
            var intermediate = ReferenceQ16(input, settings, quantizeLinear: true);
            for (var channel = 0; channel < 3; channel++)
            {
                Assert.InRange(Math.Abs(actual[channel] - reference[channel]), 0, 1);
            }
            distinguished += reference.AsSpan().SequenceEqual(intermediate) ? 0 : 1;
        }

        Assert.True(distinguished >= 2,
            "Reference vectors did not distinguish final-only from intermediate Q16 writes.");
    }

    [Fact]
    public void InGamutProductionSaturation_TracksExactSeamRatio()
    {
        ushort[] input = [20000, 25000, 22000];
        var source = OklabColor.FromEncodedRec2020(Encoded(input));
        var expected = OklabColor.ApplyChroma(source, 50, 0);
        Assert.True(OklabColor.IsInGamut(
            OklabColor.ToLinearRec2020(expected)));
        using var image = CreateRgbImage(input);

        RenderChromaStage.Apply(
            image,
            new EditSettings { Saturation = 50 });
        var actualCodes = RenderPipelineTestSupport.ReadPixels(image);
        var actual = OklabColor.FromEncodedRec2020(Encoded(actualCodes));

        Assert.InRange(Math.Abs(actual.Chroma / source.Chroma - 1.5), 0, 3e-4);
        Assert.InRange(Math.Abs(actual.Lightness - source.Lightness), 0, 2e-5);
        Assert.InRange(AngleDistance(actual.HueRadians, source.HueRadians), 0, 2e-4);
    }

    private static ushort[] ReferenceQ16(
        ushort[] input,
        EditSettings settings,
        bool quantizeLinear)
    {
        var oracle = ColorScienceOracleData.Load().Oklab;
        var toLms = ColorScienceMatrixAssertions.ToMatrix(
            oracle.MatrixRec2020ToLms);
        var toLab = ColorScienceMatrixAssertions.ToMatrix(
            oracle.MatrixLmsToOklab);
        var fromLab = PrecisionColorCases.Invert(toLab);
        var fromLms = ColorScienceMatrixAssertions.ToMatrix(
            oracle.MatrixLmsToRec2020);
        var linear = input.Select(value => ToneLut.SrgbDecode(
            value / (double)ushort.MaxValue)).ToArray();
        if (quantizeLinear)
        {
            linear = linear.Select(value =>
                Math.Round(value * ushort.MaxValue,
                    MidpointRounding.AwayFromZero) / ushort.MaxValue).ToArray();
        }
        var lms = PrecisionColorCases.Transform(toLms, linear)
            .Select(Math.Cbrt).ToArray();
        var lab = PrecisionColorCases.Transform(toLab, lms);
        var source = new Oklch(
            lab[0],
            Math.Sqrt(lab[1] * lab[1] + lab[2] * lab[2]),
            Math.Atan2(lab[2], lab[1]) % Math.Tau);
        if (source.HueRadians < 0)
        {
            source = source with { HueRadians = source.HueRadians + Math.Tau };
        }
        var adjusted = OklabColor.ApplyChroma(
            source,
            settings.Saturation,
            settings.Vibrance);
        var projected = ProjectReference(adjusted, fromLab, fromLms);
        return projected.Select(value => checked((ushort)Math.Round(
            ToneLut.SrgbEncode(value) * ushort.MaxValue,
            MidpointRounding.AwayFromZero))).ToArray();
    }

    private static double[] ProjectReference(
        Oklch color,
        double[,] fromLab,
        double[,] fromLms)
    {
        var candidate = ToLinearReference(color, fromLab, fromLms);
        if (candidate.All(value => value is >= 0 and <= 1))
        {
            return candidate;
        }
        var low = 0.0;
        var high = color.Chroma;
        var result = ToLinearReference(color with { Chroma = 0 }, fromLab, fromLms);
        for (var iteration = 0; iteration < 60; iteration++)
        {
            var middle = (low + high) * 0.5;
            var test = ToLinearReference(
                color with { Chroma = middle }, fromLab, fromLms);
            if (test.All(value => value is >= 0 and <= 1))
            {
                low = middle;
                result = test;
            }
            else
            {
                high = middle;
            }
        }
        return result;
    }

    private static double[] ToLinearReference(
        Oklch color,
        double[,] fromLab,
        double[,] fromLms)
    {
        if (color.Chroma == 0)
        {
            var neutral = Math.Pow(color.Lightness, 3);
            return [neutral, neutral, neutral];
        }
        var lab = new[]
        {
            color.Lightness,
            color.Chroma * Math.Cos(color.HueRadians),
            color.Chroma * Math.Sin(color.HueRadians)
        };
        var lms = PrecisionColorCases.Transform(fromLab, lab)
            .Select(value => value * value * value).ToArray();
        return PrecisionColorCases.Transform(fromLms, lms);
    }

    private static OklabRgb Encoded(ushort[] input) => new(
        input[0] / (double)ushort.MaxValue,
        input[1] / (double)ushort.MaxValue,
        input[2] / (double)ushort.MaxValue);

    private static MagickImage CreateRgbImage(ushort[] values)
    {
        var image = new MagickImage(MagickColors.Black, (uint)(values.Length / 3), 1)
        {
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        pixels.SetArea(0, 0, image.Width, image.Height, values);
        return image;
    }

    private static MagickImage CreateRgbaImage(ushort[] values, int? width = null)
    {
        var resolvedWidth = width ?? values.Length / 4;
        var image = new MagickImage(
            MagickColors.Transparent,
            (uint)resolvedWidth,
            (uint)(values.Length / 4 / resolvedWidth))
        {
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        pixels.SetArea(0, 0, image.Width, image.Height, values);
        return image;
    }

    private static BaseImage CreateRgbaBase(bool isRaw)
    {
        var pixels = CreateRgbaImage(
        [
            1200, 22000, 61000, 12345,
            60000, 12000, 24000, 54321
        ]);
        pixels.ColorSpace = ColorSpace.RGB;
        return new BaseImage(
            pixels,
            new BaseImageInfo(
                isRaw ? BaseSourceKind.RawLibRaw : BaseSourceKind.Standard,
                isRaw,
                BaseDecodeSettings.Default,
                null,
                null,
                6504,
                0,
                false,
                null,
                1,
                2,
                1));
    }

    private static ushort[] ReadRgba(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGBA) ??
        throw new InvalidOperationException("Unable to read RGBA pixels.");

    private static double AngleDistance(double first, double second)
    {
        var difference = Math.Abs(first - second) % Math.Tau;
        return Math.Min(difference, Math.Tau - difference);
    }
}

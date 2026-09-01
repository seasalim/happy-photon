using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderNoiseReductionTests
{
    [Theory]
    [InlineData(BaseSourceKind.RawLibRaw, true)]
    [InlineData(BaseSourceKind.Standard, false)]
    [InlineData(BaseSourceKind.HeicPlatform, false)]
    public void Zero_ReturnsBeforePixelAccessForEverySourceKind(
        BaseSourceKind kind,
        bool isRaw)
    {
        var image = new MagickImage(MagickColors.Black, 3, 2);
        image.Dispose();

        var exception = Record.Exception(() => RenderNoiseReduction.Apply(
            image,
            CreateInfo(3, 2, kind, isRaw),
            new DetailSettings { LuminanceNr = 0, ChromaNr = 0 }));

        Assert.Null(exception);
    }

    [Fact]
    public void MonochromeChromaNr_ReturnsBeforePixelAccess()
    {
        var image = new MagickImage(MagickColors.Black, 3, 2);
        image.Dispose();
        var info = CreateInfo(3, 2) with { IsMonochrome = true };

        var exception = Record.Exception(() => RenderNoiseReduction.Apply(
            image,
            info,
            new DetailSettings { ChromaNr = 100 }));

        Assert.Null(exception);
    }

    [Fact]
    public void EmptyChromaScaleSet_IsBitIdentical()
    {
        using var image = new MagickImage(MagickColors.CornflowerBlue, 3, 2);
        var before = ReadRgb(image);

        RenderNoiseReduction.Apply(
            image,
            CreateInfo(300, 200),
            new DetailSettings { ChromaNr = 100 });

        Assert.Equal(before, ReadRgb(image));
    }

    [Fact]
    public void NativeScaleMapping_DropsSubsampledAndOversizeScales()
    {
        using var full = new MagickImage(MagickColors.Black, 128, 96);
        using var preview = new MagickImage(MagickColors.Black, 128, 96);

        var fullScales = RenderNoiseReduction.ResolveScales(
            full,
            CreateInfo(128, 96),
            amount: 1);
        var previewScales = RenderNoiseReduction.ResolveScales(
            preview,
            CreateInfo(438, 329),
            amount: 1);

        Assert.Equal([1, 2, 4, 8], fullScales.Select(scale => scale.Dilation));
        Assert.Equal([1, 2], previewScales.Select(scale => scale.Dilation));
        Assert.True(previewScales[0].Threshold > previewScales[1].Threshold);
    }

    [Fact]
    public void ChromaScaleMapping_KeepsOneDeeperScaleThanLuma()
    {
        using var image = new MagickImage(MagickColors.Black, 256, 192);
        var info = CreateInfo(256, 192);

        var luma = RenderNoiseReduction.ResolveScales(image, info, 1);
        var chroma = RenderNoiseReduction.ResolveChromaScales(image, info, 1);

        Assert.Equal([1, 2, 4, 8], luma.Select(scale => scale.Dilation));
        Assert.Equal([1, 2, 4, 8, 16], chroma.Select(scale => scale.Dilation));
        Assert.True(chroma[^1].Threshold > 0);
    }

    [Fact]
    public void ChromaScaleMapping_KeepsAllResolutionMappedPreviewScales()
    {
        using var image = new MagickImage(MagickColors.Black, 1600, 1200);

        var chroma = RenderNoiseReduction.ResolveChromaScales(
            image, CreateInfo(4032, 3024), 1);

        Assert.Equal([1, 2, 4, 8], chroma.Select(scale => scale.Dilation));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    public void Apply_ChangesOnlyLuminanceIncludingAtGamutBoundaries(int value)
    {
        using var image = CreateBoundaryNoisePattern(withAlpha: true);
        var before = ReadRgba(image);

        RenderNoiseReduction.Apply(
            image,
            CreateInfo(checked((int)image.Width), checked((int)image.Height)),
            new DetailSettings { LuminanceNr = value });

        var after = ReadRgba(image);
        for (var offset = 0; offset < after.Length; offset += 4)
        {
            Assert.InRange(
                Math.Abs((after[offset + 2] - Luma(after, offset)) -
                         (before[offset + 2] - Luma(before, offset))),
                0,
                1);
            Assert.InRange(
                Math.Abs((after[offset] - Luma(after, offset)) -
                         (before[offset] - Luma(before, offset))),
                0,
                1);
            Assert.Equal(before[offset + 3], after[offset + 3]);
        }
    }

    [Fact]
    public void Apply_ReducesSeededLuminanceNoise()
    {
        using var image = CreateLuminanceNoisePattern(257, 151);
        var before = ReadRgb(image);

        RenderNoiseReduction.Apply(
            image,
            CreateInfo(257, 151),
            new DetailSettings { LuminanceNr = 50 });

        Assert.True(StandardDeviation(ReadRgb(image)) <
            StandardDeviation(before) * 0.6);
    }

    [Fact]
    public void MultipleBandsMatchSingleBandBitForBit()
    {
        using var singleBand = CreateLuminanceNoisePattern(257, 151);
        using var multipleBands = new MagickImage(singleBand);
        var info = CreateInfo(257, 151);
        var settings = new DetailSettings { LuminanceNr = 100 };

        RenderNoiseReduction.Apply(
            singleBand,
            info,
            settings,
            bandPixelLimit: int.MaxValue);
        RenderNoiseReduction.Apply(
            multipleBands,
            info,
            settings,
            bandPixelLimit: 257 * 37);

        Assert.Equal(ReadRgb(singleBand), ReadRgb(multipleBands));
    }

    [Theory]
    [InlineData(50, 0)]
    [InlineData(100, 0)]
    [InlineData(100, 100)]
    public void ChromaMultipleBandsMatchSingleBandBitForBit(
        int chromaNr,
        int luminanceNr)
    {
        using var singleBand = CreateChromaNoisePattern(257, 151);
        using var multipleBands = new MagickImage(singleBand);
        var info = CreateInfo(257, 151);
        var settings = new DetailSettings
        {
            LuminanceNr = luminanceNr,
            ChromaNr = chromaNr
        };

        RenderNoiseReduction.Apply(
            singleBand,
            info,
            settings,
            bandPixelLimit: int.MaxValue);
        RenderNoiseReduction.Apply(
            multipleBands,
            info,
            settings,
            bandPixelLimit: 257 * 37);

        Assert.Equal(ReadRgb(singleBand), ReadRgb(multipleBands));
    }

    [Fact]
    public void ChromaNr_PreservesQuantizedLumaAlphaAndGamut()
    {
        using var image = CreateChromaNoisePattern(73, 61, withAlpha: true);
        var before = ReadRgba(image);

        RenderNoiseReduction.Apply(
            image,
            CreateInfo(checked((int)image.Width), checked((int)image.Height)),
            new DetailSettings { ChromaNr = 100 });

        var after = ReadRgba(image);
        Assert.NotEqual(before, after);
        for (var offset = 0; offset < after.Length; offset += 4)
        {
            Assert.Equal(
                QuantizedLuma(before, offset),
                QuantizedLuma(after, offset));
            Assert.Equal(before[offset + 3], after[offset + 3]);
        }
    }

    [Fact]
    public void RestingPathObservesCancellationBeforePixelAccess()
    {
        var image = new MagickImage(MagickColors.Black, 3, 2);
        image.Dispose();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            RenderNoiseReduction.ApplyResting(
                image,
                CreateInfo(3, 2),
                new DetailSettings { LuminanceNr = 100 },
                RenderExecutionOptions.Resting(cancellation.Token)));
    }

    private static MagickImage CreateLuminanceNoisePattern(int width, int height)
    {
        var image = new MagickImage(
            MagickColors.Black,
            (uint)width,
            (uint)height)
        {
            ColorSpace = ColorSpace.sRGB
        };
        var random = new Random(195);
        using var pixels = image.GetPixels();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sample = checked((ushort)Math.Clamp(
                32000 + random.Next(-2400, 2401),
                0,
                ushort.MaxValue));
            pixels.SetPixel(x, y, [sample, sample, sample]);
        }
        return image;
    }

    private static MagickImage CreateChromaNoisePattern(
        int width,
        int height,
        bool withAlpha = false)
    {
        var image = new MagickImage(
            withAlpha ? MagickColors.Transparent : MagickColors.Black,
            (uint)width,
            (uint)height)
        {
            ColorSpace = ColorSpace.sRGB
        };
        var random = new Random(225);
        using var pixels = image.GetPixels();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var luma = 32000;
            var red = checked((ushort)(luma + random.Next(-5000, 5001)));
            var blue = checked((ushort)(luma + random.Next(-5000, 5001)));
            var green = checked((ushort)Math.Clamp(
                (luma - Rec2020Luminance.Red * red -
                 Rec2020Luminance.Blue * blue) /
                Rec2020Luminance.Green,
                0,
                ushort.MaxValue));
            pixels.SetPixel(
                x,
                y,
                withAlpha
                    ? [red, green, blue, checked((ushort)(y * 1000))]
                    : [red, green, blue]);
        }
        return image;
    }

    private static MagickImage CreateBoundaryNoisePattern(bool withAlpha)
    {
        var image = CreateLuminanceNoisePattern(73, 61);
        if (withAlpha)
        {
            image.Alpha(AlphaOption.Set);
        }
        using var pixels = image.GetPixels();
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var offset = (int)((x * 193 + y * 317) % 8001) - 4000;
            var red = checked((ushort)Math.Clamp(28000 + offset, 0, 65535));
            var green = checked((ushort)Math.Clamp(32000 + offset, 0, 65535));
            var blue = checked((ushort)Math.Clamp(36000 + offset, 0, 65535));
            if (x == 0)
            {
                red = 0;
                green = 4000;
                blue = 8000;
            }
            else if (x == image.Width - 1)
            {
                red = 57535;
                green = 61535;
                blue = 65535;
            }
            pixels.SetPixel(
                checked((int)x),
                checked((int)y),
                withAlpha
                    ? [red, green, blue, checked((ushort)(1000 + y * 500))]
                    : [red, green, blue]);
        }
        return image;
    }

    private static double StandardDeviation(ushort[] rgb)
    {
        var samples = new double[rgb.Length / 3];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = Luma(rgb, index * 3);
        }
        var mean = samples.Average();
        return Math.Sqrt(samples.Sum(value => (value - mean) * (value - mean)) /
            samples.Length);
    }

    private static double Luma(ushort[] values, int offset) =>
        Rec2020Luminance.Red * values[offset] +
        Rec2020Luminance.Green * values[offset + 1] +
        Rec2020Luminance.Blue * values[offset + 2];

    private static ushort QuantizedLuma(ushort[] values, int offset) =>
        checked((ushort)Math.Clamp(
            Math.Round(Luma(values, offset)),
            ushort.MinValue,
            ushort.MaxValue));

    private static ushort[] ReadRgb(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RGB pixels.");

    private static ushort[] ReadRgba(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGBA) ??
        throw new InvalidOperationException("Unable to read RGBA pixels.");

    private static BaseImageInfo CreateInfo(
        int fullWidth,
        int fullHeight,
        BaseSourceKind kind = BaseSourceKind.Standard,
        bool isRaw = false) => new(
            kind,
            isRaw,
            BaseDecodeSettings.Default,
            null,
            null,
            6504,
            0,
            false,
            null,
            1,
            fullWidth,
            fullHeight);
}

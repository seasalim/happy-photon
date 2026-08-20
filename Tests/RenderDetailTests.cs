using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderDetailTests
{
    [Fact]
    public void EffectiveSigma_ScalesFromNativeLongEdge()
    {
        using var image = new MagickImage(MagickColors.Black, 160, 100);
        var info = CreateInfo(fullWidth: 800, fullHeight: 500);

        var sigma = RenderDetail.CalculateEffectiveSigma(
            image,
            info,
            nativeSigma: 2);

        Assert.Equal(0.4, sigma, precision: 12);
    }

    [Fact]
    public void RestingTargetScale_KeepsCaptureSharpenBelowThreshold()
    {
        var info = CreateInfo(fullWidth: 7500, fullHeight: 5000);
        using var capped = new MagickImage(MagickColors.Black, 3200, 2000);
        using var fitted = new MagickImage(MagickColors.Black, 2826, 1766);

        var capSigma = RenderDetail.CalculateEffectiveSigma(
            capped,
            info,
            nativeSigma: 0.75);
        var targetSigma = RenderDetail.CalculateEffectiveSigma(
            fitted,
            info,
            nativeSigma: 0.75);

        Assert.True(capSigma >= 0.3);
        Assert.True(targetSigma < 0.3);
    }

    [Fact]
    public void Apply_SkipsPerceptuallyNilSigmaBitIdentically()
    {
        using var image = CreateChromaPattern();
        var before = RenderPipelineTestSupport.ReadPixels(image);
        var info = CreateInfo(
            fullWidth: checked((int)image.Width * 10),
            fullHeight: checked((int)image.Height * 10));

        RenderDetail.Apply(
            image,
            info,
            new DetailSettings { ChromaNr = 100 });

        Assert.Equal(before, RenderPipelineTestSupport.ReadPixels(image));
        Assert.Equal(ColorSpace.sRGB, image.ColorSpace);
    }

    [Fact]
    public void Apply_ReducesChromaVariationAndPreservesLuma()
    {
        using var image = CreateChromaPattern();
        var before = RenderPipelineTestSupport.ReadPixels(image);
        var info = CreateInfo(
            checked((int)image.Width),
            checked((int)image.Height));

        RenderDetail.Apply(
            image,
            info,
            new DetailSettings { ChromaNr = 100 });

        var after = RenderPipelineTestSupport.ReadPixels(image);
        Assert.True(
            ChromaVariation(after) < ChromaVariation(before) * 0.25);
        for (var index = 0; index < before.Length; index += 3)
        {
            Assert.InRange(
                Math.Abs(GetLuma(after, index) - GetLuma(before, index)),
                0,
                1);
        }
    }

    [Fact]
    public void Apply_PreservesAlpha()
    {
        using var image = CreateChromaPattern(withAlpha: true);
        var before = ReadAlpha(image);
        var info = CreateInfo(
            checked((int)image.Width),
            checked((int)image.Height));

        RenderDetail.Apply(
            image,
            info,
            new DetailSettings { ChromaNr = 100 },
            bandPixelLimit: checked((int)image.Width * 5));

        Assert.Equal(before, ReadAlpha(image));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public void Apply_MultipleBandsMatchSingleBandBitForBit(int chromaNr)
    {
        using var singleBand = CreateNoisePattern();
        using var multipleBands = CreateNoisePattern();
        var info = CreateInfo(
            checked((int)singleBand.Width),
            checked((int)singleBand.Height));
        var settings = new DetailSettings { ChromaNr = chromaNr };

        RenderDetail.Apply(
            singleBand,
            info,
            settings,
            bandPixelLimit: int.MaxValue);
        RenderDetail.Apply(
            multipleBands,
            info,
            settings,
            bandPixelLimit: checked((int)singleBand.Width * 37));

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(singleBand),
            RenderPipelineTestSupport.ReadPixels(multipleBands));
    }

    private static MagickImage CreateChromaPattern(bool withAlpha = false)
    {
        const int width = 32;
        const int height = 24;
        var image = new MagickImage(
            withAlpha ? MagickColors.Transparent : MagickColors.Black,
            width,
            height)
        {
            ColorSpace = ColorSpace.sRGB
        };
        using (var pixels = image.GetPixels())
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var first = (x + y) % 2 == 0;
                    if (withAlpha)
                    {
                        pixels.SetPixel(
                            x,
                            y,
                            [
                                (ushort)(first ? 40000 : 26000),
                                (ushort)(first ? 31275 : 34023),
                                (ushort)(first ? 26000 : 40000),
                                (ushort)((x + y) * 65535 / (width + height - 2))
                            ]);
                    }
                    else
                    {
                        pixels.SetPixel(
                            x,
                            y,
                            [
                                (ushort)(first ? 40000 : 26000),
                                (ushort)(first ? 31275 : 34023),
                                (ushort)(first ? 26000 : 40000)
                            ]);
                    }
                }
            }
        }
        return image;
    }

    private static MagickImage CreateNoisePattern()
    {
        const int width = 600;
        const int height = 400;
        const int channels = 3;
        var random = new Random(1729);
        var values = new ushort[width * height * channels];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = checked((ushort)random.Next(ushort.MaxValue + 1));
        }

        var image = new MagickImage(
            MagickColors.Black,
            width,
            height)
        {
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        pixels.SetArea(0, 0, width, height, values);
        return image;
    }

    private static ushort[] ReadRgba(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGBA) ??
        throw new InvalidOperationException("Unable to read RGBA pixels.");

    private static ushort[] ReadAlpha(MagickImage image)
    {
        var rgba = ReadRgba(image);
        return rgba.Where((_, index) => index % 4 == 3).ToArray();
    }

    private static double ChromaVariation(ushort[] rgb)
    {
        double total = 0;
        for (var index = 3; index < rgb.Length; index += 3)
        {
            var luma = GetLuma(rgb, index);
            var previousLuma = GetLuma(rgb, index - 3);
            total += Math.Abs(
                (rgb[index + 2] - luma) -
                (rgb[index - 1] - previousLuma));
            total += Math.Abs(
                (rgb[index] - luma) -
                (rgb[index - 3] - previousLuma));
        }
        return total;
    }

    private static double GetLuma(ushort[] rgb, int index) =>
        Rec2020Luminance.Red * rgb[index] +
        Rec2020Luminance.Green * rgb[index + 1] +
        Rec2020Luminance.Blue * rgb[index + 2];

    private static BaseImageInfo CreateInfo(int fullWidth, int fullHeight) =>
        new(
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
            fullWidth,
            fullHeight);
}

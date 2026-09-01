using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderSharpeningTests
{
    [Fact]
    public void EffectiveSigma_ScalesFromNativeLongEdge()
    {
        using var image = new MagickImage(MagickColors.Black, 160, 100);
        var info = CreateInfo(image, isRaw: false, nativeScale: 5);

        var sigma = RenderKernelSupport.CalculateEffectiveSigma(
            image,
            info,
            nativeSigma: 2);

        Assert.Equal(0.4, sigma, precision: 12);
    }

    [Fact]
    public void RestingTargetScale_KeepsCaptureSharpenBelowThreshold()
    {
        var info = CreateInfo(7500, 5000, isRaw: true);
        using var capped = new MagickImage(MagickColors.Black, 3200, 2000);
        using var fitted = new MagickImage(MagickColors.Black, 2826, 1766);

        var capSigma = RenderKernelSupport.CalculateEffectiveSigma(
            capped, info, nativeSigma: 0.75);
        var targetSigma = RenderKernelSupport.CalculateEffectiveSigma(
            fitted, info, nativeSigma: 0.75);

        Assert.True(capSigma >= 0.3);
        Assert.True(targetSigma < 0.3);
    }

    [Fact]
    public void Capture_ResolvesSourceDefaults()
    {
        using var source = CreateLumaEdge();
        using var rawDefault = new MagickImage(source);
        using var rawExplicit = new MagickImage(source);
        using var standardDefault = new MagickImage(source);
        var before = ReadRgb(source);

        RenderSharpening.ApplyCapture(
            rawDefault,
            CreateInfo(source, isRaw: true),
            new DetailSettings());
        RenderSharpening.ApplyCapture(
            rawExplicit,
            CreateInfo(source, isRaw: true),
            new DetailSettings
            {
                CaptureSharpen =
                    DetailSettings.GetCaptureSharpenDefault(
                        isRawSource: true)
            });
        RenderSharpening.ApplyCapture(
            standardDefault,
            CreateInfo(source, isRaw: false),
            new DetailSettings());

        Assert.Equal(ReadRgb(rawExplicit), ReadRgb(rawDefault));
        Assert.NotEqual(before, ReadRgb(rawDefault));
        Assert.Equal(before, ReadRgb(standardDefault));
    }

    [Fact]
    public void Capture_SkipsPerceptuallyNilSigmaBitIdentically()
    {
        using var image = CreateLumaEdge();
        var before = ReadRgb(image);
        var info = CreateInfo(
            image,
            isRaw: true,
            nativeScale: 3);

        RenderSharpening.ApplyCapture(
            image,
            info,
            new DetailSettings { CaptureSharpen = 100 });

        Assert.Equal(before, ReadRgb(image));
        Assert.Equal(ColorSpace.sRGB, image.ColorSpace);
    }

    [Fact]
    public void Capture_PreservesAlphaAndChroma()
    {
        using var image = CreateConstantChromaEdge();
        var alpha = ReadAlpha(image);
        var before = ReadRgb(image);

        RenderSharpening.ApplyCapture(
            image,
            CreateInfo(image, isRaw: false),
            new DetailSettings { CaptureSharpen = 100 });

        var after = ReadRgb(image);
        Assert.Equal(alpha, ReadAlpha(image));
        Assert.True(MaxLumaDelta(before, after) > 100);
        for (var index = 0; index < before.Length; index += 3)
        {
            var beforeLuma = GetLuma(before, index);
            var afterLuma = GetLuma(after, index);
            Assert.InRange(
                Math.Abs(
                    (before[index] - beforeLuma) -
                    (after[index] - afterLuma)),
                0,
                1);
            Assert.InRange(
                Math.Abs(
                    (before[index + 2] - beforeLuma) -
                    (after[index + 2] - afterLuma)),
                0,
                1);
        }
    }

    [Fact]
    public void Output_OnlySharpensEligibleSizedImages()
    {
        using var source = CreateLumaEdge();
        var before = ReadRgb(source);
        using var disabled = new MagickImage(source);
        using var unresized = new MagickImage(source);
        using var eligible = new MagickImage(source);
        using var tooLarge = CreateLumaEdge(width: 2561, height: 1);
        var tooLargeBefore = ReadRgb(tooLarge);

        RenderSharpening.ApplyOutput(
            disabled,
            OutputSharpeningMode.Off,
            wasResized: true);
        RenderSharpening.ApplyOutput(
            unresized,
            OutputSharpeningMode.Screen,
            wasResized: false);
        RenderSharpening.ApplyOutput(
            eligible,
            OutputSharpeningMode.Screen,
            wasResized: true);
        RenderSharpening.ApplyOutput(
            tooLarge,
            OutputSharpeningMode.Screen,
            wasResized: true);

        Assert.Equal(before, ReadRgb(disabled));
        Assert.Equal(before, ReadRgb(unresized));
        Assert.NotEqual(before, ReadRgb(eligible));
        Assert.Equal(tooLargeBefore, ReadRgb(tooLarge));
    }

    [Fact]
    public void Output_PrintIsStrongerAndAppliesWithoutResize()
    {
        using var source = CreateLumaEdge();
        var before = ReadRgb(source);
        using var screen = new MagickImage(source);
        using var print = new MagickImage(source);

        RenderSharpening.ApplyOutput(
            screen,
            OutputSharpeningMode.Screen,
            wasResized: false);
        RenderSharpening.ApplyOutput(
            print,
            OutputSharpeningMode.Print,
            wasResized: false);

        Assert.Equal(before, ReadRgb(screen));
        Assert.NotEqual(before, ReadRgb(print));

        using var resizedScreen = new MagickImage(source);
        using var resizedPrint = new MagickImage(source);
        RenderSharpening.ApplyOutput(
            resizedScreen,
            OutputSharpeningMode.Screen,
            wasResized: true);
        RenderSharpening.ApplyOutput(
            resizedPrint,
            OutputSharpeningMode.Print,
            wasResized: true);
        Assert.NotEqual(ReadRgb(resizedScreen), ReadRgb(resizedPrint));
    }

    [Theory]
    [InlineData(1600, 0.65, 0.45)]
    [InlineData(1601, 0.8, 0.5)]
    [InlineData(3201, 1.0, 0.55)]
    public void Output_PrintParametersScaleWithOutputSize(
        int longEdge,
        double expectedSigma,
        double expectedAmount)
    {
        var parameters = Assert.IsType<
            RenderSharpening.OutputSharpeningParameters>(
            RenderSharpening.ResolveOutputParameters(
                OutputSharpeningMode.Print,
                longEdge,
                wasResized: false));

        Assert.Equal(expectedSigma, parameters.Sigma);
        Assert.Equal(expectedAmount, parameters.Amount);
        Assert.Equal(0.004, parameters.Threshold);
    }

    [Fact]
    public void Capture_MultipleBandsMatchSingleBandBitForBit()
    {
        using var singleBand = CreateNoisePattern();
        using var multipleBands = new MagickImage(singleBand);
        var info = CreateInfo(singleBand, isRaw: true);
        var detail = new DetailSettings { CaptureSharpen = 100 };
        var forcedBandLimit = checked((int)singleBand.Width * 17);

        RenderSharpening.ApplyCapture(
            singleBand,
            info,
            detail,
            int.MaxValue);
        RenderSharpening.ApplyCapture(
            multipleBands,
            info,
            detail,
            forcedBandLimit);

        Assert.Equal(ReadRgba(singleBand), ReadRgba(multipleBands));
    }

    [Fact]
    public void Output_MultipleBandsMatchSingleBandBitForBit()
    {
        using var singleBand = CreateNoisePattern();
        using var multipleBands = new MagickImage(singleBand);
        var forcedBandLimit = checked((int)singleBand.Width * 19);

        RenderSharpening.ApplyOutput(
            singleBand,
            OutputSharpeningMode.Screen,
            wasResized: true,
            int.MaxValue);
        RenderSharpening.ApplyOutput(
            multipleBands,
            OutputSharpeningMode.Screen,
            wasResized: true,
            forcedBandLimit);

        Assert.Equal(ReadRgba(singleBand), ReadRgba(multipleBands));
    }

    private static MagickImage CreateLumaEdge(
        int width = 64,
        int height = 16)
    {
        var image = new MagickImage(
            MagickColors.Black,
            checked((uint)width),
            checked((uint)height))
        {
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = (ushort)(x < width / 2 ? 18000 : 47000);
                pixels.SetPixel(x, y, [value, value, value]);
            }
        }

        return image;
    }

    private static MagickImage CreateConstantChromaEdge()
    {
        const int width = 64;
        const int height = 16;
        var image = new MagickImage(
            MagickColors.Transparent,
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
                    var luma = x < width / 2 ? 22000 : 43000;
                    pixels.SetPixel(
                        x,
                        y,
                        [
                            (ushort)(luma + 4000),
                            (ushort)luma,
                            (ushort)(luma - 5000),
                            (ushort)((x + y) * 65535 / (width + height - 2))
                        ]);
                }
            }
        }

        return image;
    }

    private static MagickImage CreateNoisePattern()
    {
        const int width = 173;
        const int height = 91;
        const int channels = 4;
        var random = new Random(1729);
        var values = new ushort[width * height * channels];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = checked(
                (ushort)random.Next(ushort.MaxValue + 1));
        }

        var image = new MagickImage(
            MagickColors.Transparent,
            width,
            height)
        {
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        pixels.SetArea(0, 0, width, height, values);
        return image;
    }

    private static ushort[] ReadRgb(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RGB pixels.");

    private static double GetLuma(ushort[] rgb, int index) =>
        Rec2020Luminance.Red * rgb[index] +
        Rec2020Luminance.Green * rgb[index + 1] +
        Rec2020Luminance.Blue * rgb[index + 2];

    private static double MaxLumaDelta(
        ushort[] before,
        ushort[] after)
    {
        double maximum = 0;
        for (var index = 0; index < before.Length; index += 3)
        {
            maximum = Math.Max(
                maximum,
                Math.Abs(
                    GetLuma(after, index) -
                    GetLuma(before, index)));
        }

        return maximum;
    }

    private static ushort[] ReadAlpha(MagickImage image)
    {
        var rgba = ReadRgba(image);
        return rgba.Where((_, index) => index % 4 == 3).ToArray();
    }

    private static ushort[] ReadRgba(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGBA) ??
            throw new InvalidOperationException("Unable to read RGBA pixels.");

    private static BaseImageInfo CreateInfo(
        MagickImage image,
        bool isRaw,
        int nativeScale = 1) =>
        CreateInfo(
            checked((int)image.Width * nativeScale),
            checked((int)image.Height * nativeScale),
            isRaw);

    private static BaseImageInfo CreateInfo(
        int width,
        int height,
        bool isRaw) =>
        new(
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
            width,
            height);
}

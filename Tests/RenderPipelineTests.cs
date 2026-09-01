using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderPipelineTests
{
    private readonly RenderPipeline _pipeline = new();

    [Fact]
    public void Render_IsDeterministicAndDoesNotMutateBase()
    {
        using var baseImage = CreateGradientBase();
        var before = RenderPipelineTestSupport.ReadPixels(baseImage.Pixels);
        var settings = new EditSettings
        {
            Exposure = 0.75,
            Brightness = 10,
            Contrast = 20,
            Shadows = 30,
            Highlights = -40,
            Saturation = 15,
            Vibrance = -10,
            Detail = new DetailSettings { ChromaNr = 100 },
            Rotation = 90
        };

        using var first = Render(settings, baseImage);
        using var second = Render(settings.Clone(), baseImage);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(first.Image),
            RenderPipelineTestSupport.ReadPixels(second.Image));
        Assert.Equal(before, RenderPipelineTestSupport.ReadPixels(baseImage.Pixels));
        Assert.Equal(8u, baseImage.Pixels.Width);
        Assert.Equal(4u, baseImage.Pixels.Height);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_SourceExposureBiasMatchesUserExposure(bool isRaw)
    {
        ushort[] samples =
        [
            1000, 2000, 3000,
            12000, 18000, 24000,
            30000, 40000, 50000
        ];
        using var biasedBase = RenderPipelineTestSupport.CreateBase(
            samples,
            isRaw,
            sourceBiasEv: 1);
        using var exposedBase = RenderPipelineTestSupport.CreateBase(
            samples,
            isRaw);

        using var biased = Render(new EditSettings(), biasedBase);
        using var exposed = Render(
            new EditSettings { Exposure = 1 },
            exposedBase);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(exposed.Image),
            RenderPipelineTestSupport.ReadPixels(biased.Image));
    }

    [Fact]
    public void Render_CustomWhiteBalanceChangesPixels()
    {
        using var baseImage = CreateGradientBase();
        var custom = new EditSettings
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = 3000,
                Tint = 50
            }
        };

        using var identity = Render(new EditSettings(), baseImage);
        using var adjusted = Render(custom, baseImage);

        Assert.NotEqual(
            RenderPipelineTestSupport.ReadPixels(identity.Image),
            RenderPipelineTestSupport.ReadPixels(adjusted.Image));
    }

    [Fact]
    public void Render_HigherKelvinWarmsNeutralPixel()
    {
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            [10000, 10000, 10000]);
        var settings = new EditSettings
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = 9000,
                Tint = 0
            }
        };

        using var adjusted = Render(settings, baseImage);
        var pixels = RenderPipelineTestSupport.ReadPixels(adjusted.Image);

        Assert.True(pixels[0] > pixels[2]);
    }

    [Fact]
    public void Render_PositiveTintSuppressesGreen()
    {
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            [10000, 10000, 10000]);
        using var neutral = Render(
            CreateCustomWhiteBalance(6504, 0),
            baseImage);
        using var magenta = Render(
            CreateCustomWhiteBalance(6504, 50),
            baseImage);
        var neutralPixels = RenderPipelineTestSupport.ReadPixels(neutral.Image);
        var magentaPixels = RenderPipelineTestSupport.ReadPixels(magenta.Image);

        Assert.True(magentaPixels[1] < neutralPixels[1]);
    }

    [Fact]
    public void Render_AsShotBypassIsBitIdentical()
    {
        using var baseImage = CreateGradientBase();
        var asShot = new EditSettings
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.AsShot,
                Kelvin = 3000,
                Tint = 50,
                Gains = [2, 1, 0.5]
            }
        };

        using var baseline = Render(new EditSettings(), baseImage);
        using var bypassed = Render(asShot, baseImage);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(baseline.Image),
            RenderPipelineTestSupport.ReadPixels(bypassed.Image));
    }

    [Fact]
    public void Render_PickedGainsApplyRequestedChannelRatios()
    {
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            [8000, 8000, 8000]);
        var settings = new EditSettings
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Picked,
                Gains = [2, 1, 0.5]
            }
        };

        using var adjusted = Render(settings, baseImage);
        var pixels = RenderPipelineTestSupport.ReadPixels(adjusted.Image);

        Assert.True(pixels[0] > pixels[1]);
        Assert.True(pixels[1] > pixels[2]);
    }

    [Fact]
    public void Render_WhiteBalancePreservesAlpha()
    {
        var pixels = new MagickImage(MagickColors.Transparent, 1, 1)
        {
            ColorSpace = ColorSpace.RGB
        };
        using (var pixelCollection = pixels.GetPixels())
        {
            pixelCollection.SetPixel(0, 0, [8000, 10000, 12000, 32768]);
        }
        using var baseImage = new BaseImage(
            pixels,
            new BaseImageInfo(
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
                1,
                1));
        var settings = new EditSettings
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = 9000,
                Tint = 25
            }
        };

        using var adjusted = Render(settings, baseImage);
        var rgba = adjusted.Image.GetPixelsUnsafe()
            .ToShortArray(PixelMapping.RGBA);

        Assert.NotNull(rgba);
        Assert.True(adjusted.Image.HasAlpha);
        Assert.Equal(32768, rgba![3]);
    }

    [Fact]
    public void Render_CombinedChromaUsesPerPixelWeighting()
    {
        using var baseImage = CreateGradientBase();
        var combined = new EditSettings
        {
            Saturation = 100,
            Vibrance = -100
        };

        using var identity = Render(new EditSettings(), baseImage);
        using var adjusted = Render(combined, baseImage);

        Assert.NotEqual(
            RenderPipelineTestSupport.ReadPixels(identity.Image),
            RenderPipelineTestSupport.ReadPixels(adjusted.Image));
    }

    [Fact]
    public void Render_AppliesChromaToEncodedDisplayRec2020Pixels()
    {
        using var baseImage = RenderPipelineTestSupport.CreateBase(
        [
            6000, 18000, 42000,
            50000, 21000, 9000
        ]);
        using var neutral = RenderShared(new EditSettings(), baseImage);
        var expected = RenderPipelineTestSupport.ReadPixels(neutral);
        for (var offset = 0; offset < expected.Length; offset += 3)
        {
            var transformed = OklabColor.TransformEncodedRec2020(
                new OklabRgb(
                    expected[offset] / (double)ushort.MaxValue,
                    expected[offset + 1] / (double)ushort.MaxValue,
                    expected[offset + 2] / (double)ushort.MaxValue),
                saturation: 50,
                vibrance: 0);
            expected[offset] = Encode(transformed.Red);
            expected[offset + 1] = Encode(transformed.Green);
            expected[offset + 2] = Encode(transformed.Blue);
        }

        using var saturated = RenderShared(
            new EditSettings { Saturation = 50 },
            baseImage);

        Assert.Equal(ColorSpace.sRGB, saturated.ColorSpace);
        Assert.Equal(
            expected,
            RenderPipelineTestSupport.ReadPixels(saturated));
    }

    [Fact]
    public void Render_AppliesChromaNoiseReductionAfterChromaStage()
    {
        using var baseImage = CreateGradientBase();
        var settings = new EditSettings
        {
            Saturation = 30,
            Detail = new DetailSettings { ChromaNr = 100 }
        };
        using var chromaOnly = RenderShared(
            new EditSettings { Saturation = 30 },
            baseImage);
        using var expected = new MagickImage(chromaOnly);
        RenderNoiseReduction.Apply(expected, baseImage.Info, settings.Detail);

        using var actual = RenderShared(settings, baseImage);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(expected),
            RenderPipelineTestSupport.ReadPixels(actual));
    }

    [Fact]
    public void Render_DetailOnlyForcedBandsDoesNotMutateBase()
    {
        const int width = 8;
        const int height = 12;
        var random = new Random(1729);
        var samples = new ushort[width * height * 3];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = checked((ushort)random.Next(ushort.MaxValue + 1));
        }
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            samples,
            height: height);
        var before = RenderPipelineTestSupport.ReadPixels(baseImage.Pixels);
        var request = new RenderRequest(
            baseImage,
            new EditSettings
            {
                Detail = new DetailSettings { ChromaNr = 100 }
            },
            RenderIntent.Export,
            null,
            new RenderOptions());

        using var singleBand = _pipeline.Render(request, int.MaxValue);
        using var multipleBands = _pipeline.Render(request, width * 3);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(singleBand.Image),
            RenderPipelineTestSupport.ReadPixels(multipleBands.Image));
        Assert.Equal(before, RenderPipelineTestSupport.ReadPixels(baseImage.Pixels));
    }

    [Fact]
    public void Render_PreviewAndExportSharePixelMathAndExportSuppressesMask()
    {
        using var baseImage = CreateGradientBase();
        var settings = new EditSettings { Exposure = 1 };
        var options = new RenderOptions(
            ComputeStats: true,
            ComputeOverlayMasks: true);

        using var preview = _pipeline.Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            null,
            options));
        using var export = _pipeline.Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Export,
            null,
            options));

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(preview.Image),
            RenderPipelineTestSupport.ReadPixels(export.Image));
        Assert.NotNull(preview.OverlayMask);
        Assert.Null(export.OverlayMask);
        Assert.Equal(preview.Clipping, export.Clipping);
    }

    [Fact]
    public void Render_MaxDimensionDownscalesAndReturnsSrgb()
    {
        using var baseImage = CreateGradientBase();

        using var result = _pipeline.Render(new RenderRequest(
            baseImage,
            new EditSettings(),
            RenderIntent.Preview,
            4,
            new RenderOptions(false, false)));

        Assert.Equal(4u, result.Image.Width);
        Assert.Equal(2u, result.Image.Height);
        Assert.Equal(ColorSpace.sRGB, result.Image.ColorSpace);
    }

    [Fact]
    public void RetagAsSrgb_PreservesSamplesAndAlpha()
    {
        using var source = new MagickImage(MagickColors.Transparent, 2, 1)
        {
            ColorSpace = ColorSpace.RGB
        };
        using (var pixels = source.GetPixels())
        {
            pixels.SetPixel(0, 0, [1000, 2000, 3000, 0]);
            pixels.SetPixel(1, 0, [4000, 5000, 6000, 32768]);
        }
        var before = source.GetPixelsUnsafe().ToShortArray(PixelMapping.RGBA) ??
            throw new InvalidOperationException("Unable to read RGBA pixels.");

        RenderColorEncoding.RetagAsSrgb(source);
        var actual = source.GetPixelsUnsafe().ToShortArray(PixelMapping.RGBA) ??
            throw new InvalidOperationException("Unable to read RGBA pixels.");

        Assert.Equal(ColorSpace.sRGB, source.ColorSpace);
        Assert.True(source.HasAlpha);
        Assert.Equal(before, actual);
    }

    [Fact]
    public void ResizeInLinearLight_AveragesBlackAndWhiteInLinearDomain()
    {
        using var image = new MagickImage(MagickColors.Black, 2, 1)
        {
            ColorSpace = ColorSpace.sRGB
        };
        using (var pixels = image.GetPixels())
        {
            pixels.SetPixel(1, 0, [ushort.MaxValue, ushort.MaxValue, ushort.MaxValue]);
        }

        RenderColorEncoding.ResizeInLinearLight(image, 1);

        var actual = RenderPipelineTestSupport.ReadPixels(image)[0];
        var expected = ToneLut.SrgbEncode(0.5) * ushort.MaxValue;
        Assert.InRange(Math.Abs(actual - expected), 0, 2);
    }

    [Fact]
    public void RenderResult_DisposeIsIdempotentAndReleasesOwnedImages()
    {
        using var baseImage = CreateGradientBase();
        var result = Render(new EditSettings(), baseImage);

        result.Dispose();
        result.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = result.Image);
        Assert.Throws<ObjectDisposedException>(() => _ = result.OverlayMask);
    }

    private RenderResult Render(EditSettings settings, BaseImage baseImage) =>
        _pipeline.Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            null,
            new RenderOptions()));

    private MagickImage RenderShared(
        EditSettings settings,
        BaseImage baseImage) =>
        _pipeline.RenderDisplayRec2020(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            null,
            new RenderOptions()));

    private static EditSettings CreateCustomWhiteBalance(
        double kelvin,
        double tint) =>
        new()
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = kelvin,
                Tint = tint
            }
        };

    private static BaseImage CreateGradientBase()
    {
        var samples = new ushort[8 * 4 * 3];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (ushort)(i * 65535 / (samples.Length - 1));
        }

        return RenderPipelineTestSupport.CreateBase(samples, height: 4);
    }

    private static ushort Encode(double value) =>
        (ushort)Math.Round(
            value * ushort.MaxValue,
            MidpointRounding.AwayFromZero);
}

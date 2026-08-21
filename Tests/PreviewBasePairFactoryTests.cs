using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PreviewBasePairFactoryTests
{
    // The no-version-bump contract: both bases derive INDEPENDENTLY from the
    // decoded buffer with the same one-step resize the pre-pair loader used,
    // so the 1600-class pixels are byte-identical to the old derivation and
    // the large pixels are exactly the independent large resize.
    [Fact]
    public void Create_DerivesBothBasesByIndependentOneStepResizes()
    {
        using var decoded = CreateNoise(4000, 2600);
        using var expectedInteractive = new MagickImage(decoded);
        BitmapConversionService.ResizeToMaxDimension(
            expectedInteractive,
            BaseImage.InteractivePreviewMaxDimension);
        using var expectedLarge = new MagickImage(decoded);
        BitmapConversionService.ResizeToMaxDimension(
            expectedLarge,
            BaseImage.LargePreviewMaxDimension);

        using var pair = PreviewBasePairFactory.Create(
            decoded,
            CreateInfo(4000, 2600),
            CancellationToken.None);

        Assert.Equal(
            ReadPixels(expectedInteractive),
            ReadPixels(pair.Interactive.Pixels));
        Assert.NotNull(pair.Large);
        Assert.Equal(
            ReadPixels(expectedLarge),
            ReadPixels(pair.Large!.Pixels));
    }

    [Fact]
    public void Create_SmallDecodeKeepsBothBasesAndMaskOnlyOnInteractive()
    {
        using var decoded = CreateNoise(1200, 800);
        var sourceSaturation = new SourceSaturationMask(1200, 800);
        using var pair = PreviewBasePairFactory.Create(
            decoded,
            CreateInfo(1200, 800),
            CancellationToken.None,
            sourceSaturation);

        Assert.Equal(1200u, pair.Interactive.Pixels.Width);
        Assert.Same(sourceSaturation, pair.Interactive.SourceSaturation);
        Assert.NotNull(pair.Large);
        Assert.Equal(1200u, pair.Large!.Pixels.Width);
        Assert.Null(pair.Large.SourceSaturation);
        Assert.Equal(
            ReadPixels(pair.Interactive.Pixels),
            ReadPixels(pair.Large!.Pixels));
    }

    private static MagickImage CreateNoise(int width, int height)
    {
        var random = new Random(159159);
        var samples = new ushort[width * height * 3];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = checked((ushort)random.Next(0, 65536));
        }
        var image = new MagickImage();
        var settings = new PixelReadSettings(
            (uint)width,
            (uint)height,
            StorageType.Short,
            PixelMapping.RGB);
        image.ReadPixels(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                samples.AsSpan()),
            settings);
        image.ColorSpace = ColorSpace.RGB;
        return image;
    }

    private static BaseImageInfo CreateInfo(int width, int height) =>
        new(
            BaseSourceKind.RawLibRaw,
            IsRawSource: true,
            BaseDecodeSettings.Default,
            null,
            null,
            5500,
            0,
            false,
            null,
            1,
            width,
            height);

    private static byte[] ReadPixels(MagickImage image)
    {
        using var pixels = image.GetPixels();
        return pixels.ToByteArray(PixelMapping.RGB)!;
    }
}

using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WhiteBalanceSamplingTests
{
    [Fact]
    public void PickGains_UsesFiveByFiveLinearMean()
    {
        using var image = CreateImage(9, 9, 0.25, 0.5, 0.125);

        var gains = WhiteBalanceSampling.PickGains(
            image,
            new HappyPhoton.Models.EditSettings(),
            0.5,
            0.5);

        Assert.NotNull(gains);
        Assert.Equal(2, gains![0], 6);
        Assert.Equal(1, gains[1]);
        Assert.Equal(4, gains[2], 6);
    }

    [Theory]
    [InlineData(0.001, 0.5, 0.5)]
    [InlineData(0.5, 0.96, 0.5)]
    public void PickGains_RejectsNoiseFloorOrClippedRegion(
        double red,
        double green,
        double blue)
    {
        using var image = CreateImage(5, 5, red, green, blue);

        Assert.Null(WhiteBalanceSampling.PickGains(
            image,
            new HappyPhoton.Models.EditSettings(),
            0.5,
            0.5));
    }

    [Fact]
    public void PickGains_ValidatesRegionMeanNotIndividualPixels()
    {
        using var image = CreateImage(5, 5, 0.25, 0.5, 0.125);
        using (var pixels = image.GetPixels())
        {
            pixels.SetPixel(0, 0, [ushort.MaxValue, ushort.MaxValue, ushort.MaxValue]);
        }

        var gains = WhiteBalanceSampling.PickGains(
            image,
            new HappyPhoton.Models.EditSettings(),
            0.5,
            0.5);

        Assert.NotNull(gains);
    }

    [Fact]
    public void AutoGains_DropsInvalidPixelsAndIsDeterministic()
    {
        using var image = CreateImage(64, 64, 0.25, 0.5, 0.125);
        using (var pixels = image.GetPixels())
        {
            pixels.SetPixel(0, 0, [ushort.MaxValue, ushort.MaxValue, ushort.MaxValue]);
            pixels.SetPixel(1, 0, [0, 0, 0]);
        }

        var first = WhiteBalanceSampling.AutoGains(image);
        var second = WhiteBalanceSampling.AutoGains(image);

        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(2, first![0], 3);
        Assert.Equal(1, first[1]);
        Assert.Equal(4, first[2], 3);
    }

    private static MagickImage CreateImage(
        uint width,
        uint height,
        double red,
        double green,
        double blue)
    {
        var image = new MagickImage(MagickColors.Black, width, height)
        {
            ColorSpace = ColorSpace.RGB,
            Depth = 16
        };
        using var pixels = image.GetPixels();
        var values = new[]
        {
            (ushort)Math.Round(red * ushort.MaxValue),
            (ushort)Math.Round(green * ushort.MaxValue),
            (ushort)Math.Round(blue * ushort.MaxValue)
        };
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels.SetPixel((int)x, (int)y, values);
            }
        }

        return image;
    }
}

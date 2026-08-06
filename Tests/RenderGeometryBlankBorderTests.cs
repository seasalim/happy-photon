using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public class RenderGeometryBlankBorderTests
{
    [Theory]
    [InlineData(400, 300, 0.5)]
    [InlineData(400, 300, 1.0)]
    [InlineData(400, 300, 3.0)]
    [InlineData(400, 300, 7.5)]
    [InlineData(400, 300, 15.0)]
    [InlineData(400, 300, 30.0)]
    [InlineData(400, 300, 44.0)]
    [InlineData(400, 300, -3.0)]
    [InlineData(400, 300, -12.0)]
    [InlineData(300, 400, 3.0)]
    [InlineData(300, 400, 44.0)]
    [InlineData(511, 293, 0.5)]
    [InlineData(511, 293, -12.0)]
    public void HorizonRotationWithoutCrop_LeavesNoBlankBorders(uint width, uint height, double degrees)
    {
        using var image = CreateRedImage(width, height);

        RenderGeometry.Apply(
            image,
            new EditSettings { HorizonRotation = degrees });

        Assert.True(image.Width < width && image.Height < height,
            $"Expected auto-crop smaller than source, got {image.Width}x{image.Height}");
        Assert.Equal(0, CountBlankBorderPixels(image));
    }

    [Fact]
    public void HorizonRotationWithUserCrop_LeavesNoBlankPixels()
    {
        using var image = CreateRedImage(400, 300);
        var settings = new EditSettings
        {
            HorizonRotation = 5.0,
            Crop = new CropRegion { Left = 0.0, Top = 0.0, Right = 0.6, Bottom = 0.7 }
        };

        RenderGeometry.Apply(image, settings);

        Assert.Equal(0, CountBlankPixels(image));
    }

    [Fact]
    public void CropWithoutRotation_ProducesExpectedSize()
    {
        using var image = CreateRedImage(400, 300);
        var settings = new EditSettings
        {
            Crop = new CropRegion { Left = 0.25, Top = 0.25, Right = 0.75, Bottom = 0.75 }
        };

        RenderGeometry.Apply(image, settings);

        Assert.Equal(200u, image.Width);
        Assert.Equal(150u, image.Height);
        Assert.Equal(0, CountBlankPixels(image));
    }

    private static MagickImage CreateRedImage(uint width, uint height)
    {
        var image = new MagickImage(MagickColors.Red, width, height);
        image.BackgroundColor = MagickColors.White;
        return image;
    }

    private static bool IsBlank(IMagickColor<ushort> color) =>
        !(color.R > Quantum.Max / 2 && color.G < Quantum.Max / 2 && color.B < Quantum.Max / 2);

    private static int CountBlankBorderPixels(MagickImage image)
    {
        using var pixels = image.GetPixels();
        var blank = 0;
        for (int x = 0; x < image.Width; x++)
        {
            if (IsBlank(pixels.GetPixel(x, 0).ToColor()!)) blank++;
            if (IsBlank(pixels.GetPixel(x, (int)image.Height - 1).ToColor()!)) blank++;
        }
        for (int y = 0; y < image.Height; y++)
        {
            if (IsBlank(pixels.GetPixel(0, y).ToColor()!)) blank++;
            if (IsBlank(pixels.GetPixel((int)image.Width - 1, y).ToColor()!)) blank++;
        }
        return blank;
    }

    private static int CountBlankPixels(MagickImage image)
    {
        using var pixels = image.GetPixels();
        var blank = 0;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            if (IsBlank(pixels.GetPixel(x, y).ToColor()!)) blank++;
        }
        return blank;
    }
}

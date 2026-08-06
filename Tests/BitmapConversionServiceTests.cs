using ImageMagick;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class BitmapConversionServiceTests
{
    private readonly AvaloniaTestFixture _fixture;

    public BitmapConversionServiceTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [WindowsFact]
    public void ConvertToMagickImage_PreservesDimensionsAndChannels()
    {
        _fixture.RequireWindows();
        using var source = new MagickImage(MagickColors.Red, 3, 2);
        using var bitmap = BitmapConversionService.ConvertToBitmap(source);

        using var converted = BitmapConversionService.ConvertToMagickImage(bitmap!);
        var pixel = converted.GetPixelsUnsafe().GetPixel(0, 0).ToColor();

        Assert.Equal(3u, converted.Width);
        Assert.Equal(2u, converted.Height);
        Assert.NotNull(pixel);
        Assert.True(pixel!.R > pixel.G);
        Assert.True(pixel.R > pixel.B);
    }
}

using ImageMagick;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class HistogramServiceBitmapTests
{
    private readonly AvaloniaTestFixture _fixture;

    public HistogramServiceBitmapTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [WindowsFact]
    public void CalculateHistogram_ReadsBitmapPixelsWithoutImageDecode()
    {
        _fixture.RequireWindows();
        using var source = new MagickImage(MagickColors.Red, 1, 1);
        using var bitmap = BitmapConversionService.ConvertToBitmap(source);
        var service = new HistogramService();

        var histogram = service.CalculateHistogram(bitmap!);

        Assert.Equal(1, histogram.Red[255]);
        Assert.Equal(1, histogram.Green[0]);
        Assert.Equal(1, histogram.Blue[0]);
        Assert.Equal(1, histogram.Luminance[76]);
    }
}

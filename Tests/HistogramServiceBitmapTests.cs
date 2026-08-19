using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
        Assert.Null(histogram.Waveform);
    }

    [WindowsFact]
    public void LibrarySnapshotBoundsLargeBitmapToCanonicalLongEdge()
    {
        _fixture.RequireWindows();
        using var bitmap = CreateRedBitmap(new PixelSize(512, 341), 144);

        using var snapshot = HistogramService.CreateLibrarySnapshot(bitmap);
        var histogram = new HistogramService().CalculateHistogram(snapshot);
        var pixelCount = snapshot.PixelSize.Width * snapshot.PixelSize.Height;

        Assert.Equal(
            HistogramService.LibraryHistogramDimension,
            Math.Max(snapshot.PixelSize.Width, snapshot.PixelSize.Height));
        Assert.Equal(pixelCount, histogram.Red[255]);
        Assert.Equal(pixelCount, histogram.Green[0]);
        Assert.Equal(pixelCount, histogram.Blue[0]);
    }

    private static WriteableBitmap CreateRedBitmap(PixelSize size, double dpi)
    {
        var bitmap = new WriteableBitmap(
            size,
            new Vector(dpi, dpi),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);
        using var buffer = bitmap.Lock();
        var pixels = new byte[buffer.RowBytes * size.Height];
        for (var y = 0; y < size.Height; y++)
        {
            for (var x = 0; x < size.Width; x++)
            {
                var offset = y * buffer.RowBytes + x * 4;
                pixels[offset + 2] = 255;
                pixels[offset + 3] = 255;
            }
        }
        Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        return bitmap;
    }
}

using Avalonia.Media.Imaging;
using ImageMagick;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Service for calculating image histograms using optimized batch pixel access.
/// </summary>
public class HistogramService
{
    private const int HistogramMaxDimension = 1024;

    public void CalculateHistogram(MagickImage image, HistogramData histogram)
    {
        using var histogramImage = CreateHistogramImage(image);

        using var pixels = histogramImage.GetPixelsUnsafe();
        var data = pixels.ToByteArray(PixelMapping.RGB);

        if (data == null) return;

        var bytesPerPixel = 6; // RGB with 2 bytes each for Q16
        var pixelCount = data.Length / bytesPerPixel;

        for (int i = 0; i < pixelCount; i++)
        {
            var offset = i * bytesPerPixel;

            var r = data[offset + 1];
            var g = data[offset + 3];
            var b = data[offset + 5];

            histogram.Red[r]++;
            histogram.Green[g]++;
            histogram.Blue[b]++;

            var lum = (int)(0.299 * r + 0.587 * g + 0.114 * b);
            lum = Math.Clamp(lum, 0, 255);
            histogram.Luminance[lum]++;
        }

        histogram.Normalize();
    }

    public HistogramData CalculateHistogram(Bitmap bitmap)
    {
        var histogram = new HistogramData();
        var data = BitmapConversionService.CopyBgraPixels(bitmap);
        for (var offset = 0; offset < data.Length; offset += 4)
        {
            var blue = data[offset];
            var green = data[offset + 1];
            var red = data[offset + 2];
            histogram.Red[red]++;
            histogram.Green[green]++;
            histogram.Blue[blue]++;
            var luminance = (int)(0.299 * red + 0.587 * green + 0.114 * blue);
            histogram.Luminance[Math.Clamp(luminance, 0, 255)]++;
        }
        histogram.Normalize();
        return histogram;
    }

    private static MagickImage CreateHistogramImage(MagickImage source)
    {
        if (source.Width <= (uint)HistogramMaxDimension && source.Height <= (uint)HistogramMaxDimension)
            return new MagickImage(source);

        var clone = new MagickImage(source);
        BitmapConversionService.ResizeToMaxDimension(clone, HistogramMaxDimension);
        return clone;
    }
}

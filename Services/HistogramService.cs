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

    public void CalculateHistogram(RenderResult result, HistogramData histogram)
    {
        ArgumentNullException.ThrowIfNull(result);
        CalculateHistogram(result.Image, histogram);
    }

    private static void CalculateHistogram(
        MagickImage image,
        HistogramData histogram)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(histogram);
        using var histogramImage = CreateHistogramImage(image);
        using var pixels = histogramImage.GetPixelsUnsafe();
        var data = pixels.ToShortArray(PixelMapping.RGB);

        if (data == null) return;

        for (var offset = 0; offset < data.Length; offset += 3)
        {
            var r = data[offset] >> 8;
            var g = data[offset + 1] >> 8;
            var b = data[offset + 2] >> 8;

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
        ArgumentNullException.ThrowIfNull(bitmap);
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
        {
            return new MagickImage(source);
        }

        var clone = new MagickImage(source);
        BitmapConversionService.ResizeToMaxDimension(clone, HistogramMaxDimension);
        return clone;
    }
}

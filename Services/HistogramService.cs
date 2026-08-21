using Avalonia;
using Avalonia.Media.Imaging;
using ImageMagick;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Service for calculating image histograms using optimized batch pixel access.
/// </summary>
public class HistogramService
{
    internal const int HistogramMaxDimension = 1024;
    internal const int LibraryHistogramDimension = 150;

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
        // Only clone when a downscale is required; reads never mutate the
        // source, so small frames are sampled in place.
        using var resized =
            image.Width > (uint)HistogramMaxDimension ||
            image.Height > (uint)HistogramMaxDimension
                ? CreateHistogramImage(image)
                : null;
        var histogramImage = resized ?? image;
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

        histogram.Waveform = WaveformAccumulator.Accumulate(
            data,
            (int)histogramImage.Width,
            (int)histogramImage.Height);
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

    public HistogramData CalculateLibraryHistogram(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using var snapshot = CreateLibrarySnapshot(bitmap);
        return CalculateHistogram(snapshot);
    }

    internal static Bitmap CreateLibrarySnapshot(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var scale = LibraryHistogramDimension /
            (double)Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        var size = new PixelSize(
            Math.Max(1, (int)Math.Round(bitmap.PixelSize.Width * scale)),
            Math.Max(1, (int)Math.Round(bitmap.PixelSize.Height * scale)));
        if (bitmap is not WriteableBitmap)
        {
            return bitmap.CreateScaledBitmap(
                size,
                BitmapInterpolationMode.MediumQuality);
        }

        using var image = BitmapConversionService.ConvertToMagickImage(bitmap);
        image.Resize(new MagickGeometry((uint)size.Width, (uint)size.Height)
        {
            IgnoreAspectRatio = true
        });
        return BitmapConversionService.ConvertToBitmap(image)!;
    }

    private static MagickImage CreateHistogramImage(MagickImage source)
    {
        var clone = new MagickImage(source);
        BitmapConversionService.ResizeToMaxDimension(clone, HistogramMaxDimension);
        return clone;
    }
}

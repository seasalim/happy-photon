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
    private const int MinimumParallelPixels = 512 * 512;
    private static readonly double[] RedLuminance = CreateLuminanceTable(0.299);
    private static readonly double[] GreenLuminance = CreateLuminanceTable(0.587);
    private static readonly double[] BlueLuminance = CreateLuminanceTable(0.114);

    public void CalculateHistogram(RenderResult result, HistogramData histogram)
    {
        ArgumentNullException.ThrowIfNull(result);
        CalculateHistogram(result.Image, histogram);
    }

    internal static MagickImage CreateSamplingSnapshot(MagickImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new MagickImage(image);
    }

    internal static void CalculateHistogramFromSnapshot(
        MagickImage snapshot,
        HistogramData histogram)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        BitmapConversionService.ResizeToMaxDimension(
            snapshot,
            HistogramMaxDimension);
        CalculateHistogramCore(snapshot, histogram);
    }

    internal static void ResizeSamplingSnapshot(MagickImage snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        BitmapConversionService.ResizeToMaxDimension(
            snapshot,
            HistogramMaxDimension);
    }

    internal static void CalculateHistogramFromPreparedSnapshot(
        MagickImage snapshot,
        HistogramData histogram,
        bool includeHistogram = true,
        bool includeWaveform = true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(histogram);
        CalculateHistogramCore(
            snapshot,
            histogram,
            includeHistogram,
            includeWaveform);
    }

    internal static void CalculatePreviewHistogram(
        byte[] bgra,
        int width,
        int height,
        HistogramData histogram,
        bool includeHistogram,
        bool includeWaveform)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        ArgumentNullException.ThrowIfNull(histogram);
        if (bgra.Length != checked(width * height * 4))
        {
            throw new ArgumentException(
                "The BGRA buffer length must match its dimensions.",
                nameof(bgra));
        }

        var waveform = includeWaveform ? new WaveformData() : null;
        var columns = includeWaveform ? new int[width] : null;
        if (columns != null)
        {
            for (var x = 0; x < width; x++)
            {
                columns[x] = x * WaveformData.ColumnCount / width;
            }
        }

        var workers = bgra.Length / 4 >= MinimumParallelPixels
            ? Math.Min(2, height)
            : 1;
        if (workers == 1)
        {
            AccumulatePreviewRows(
                bgra,
                width,
                0,
                height,
                includeHistogram ? histogram.Red : null,
                includeHistogram ? histogram.Green : null,
                includeHistogram ? histogram.Blue : null,
                includeHistogram ? histogram.Luminance : null,
                waveform,
                columns);
        }
        else
        {
            var partials = new StatsBuffer[workers];
            Parallel.For(0, workers, worker =>
            {
                var partial = new StatsBuffer(includeWaveform);
                partials[worker] = partial;
                AccumulatePreviewRows(
                    bgra,
                    width,
                    height * worker / workers,
                    height * (worker + 1) / workers,
                    includeHistogram ? partial.Red : null,
                    includeHistogram ? partial.Green : null,
                    includeHistogram ? partial.Blue : null,
                    includeHistogram ? partial.Luminance : null,
                    partial.Waveform,
                    columns);
            });
            foreach (var partial in partials)
            {
                if (includeHistogram)
                {
                    Merge(histogram.Red, partial.Red);
                    Merge(histogram.Green, partial.Green);
                    Merge(histogram.Blue, partial.Blue);
                    Merge(histogram.Luminance, partial.Luminance);
                }
                if (waveform != null)
                {
                    Merge(waveform.Luminance, partial.Waveform!.Luminance);
                    Merge(
                        waveform.ColumnSampleCounts,
                        partial.Waveform.ColumnSampleCounts);
                }
            }
        }

        if (waveform != null && width < WaveformData.ColumnCount)
        {
            BackFillWaveformColumns(waveform);
        }
        histogram.Waveform = waveform;
        if (includeHistogram)
        {
            histogram.Normalize();
        }
    }

    private static void BackFillWaveformColumns(WaveformData waveform)
    {
        var sourceColumn = 0;
        for (var column = 1; column < WaveformData.ColumnCount; column++)
        {
            if (waveform.ColumnSampleCounts[column] != 0)
            {
                sourceColumn = column;
                continue;
            }
            waveform.ColumnSampleCounts[column] =
                waveform.ColumnSampleCounts[sourceColumn];
            for (var level = 0; level < WaveformData.LevelCount; level++)
            {
                waveform.Luminance[level * WaveformData.ColumnCount + column] =
                    waveform.Luminance[
                        level * WaveformData.ColumnCount + sourceColumn];
            }
        }
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
        CalculateHistogramCore(resized ?? image, histogram);
    }

    private static void CalculateHistogramCore(
        MagickImage histogramImage,
        HistogramData histogram,
        bool includeHistogram = true,
        bool includeWaveform = true)
    {
        using var pixels = histogramImage.GetPixelsUnsafe();
        var data = pixels.ToShortArray(PixelMapping.RGB);

        if (data == null) return;

        var waveform = includeWaveform ? new WaveformData() : null;
        AccumulateStats(
            data,
            (int)histogramImage.Width,
            (int)histogramImage.Height,
            histogram,
            waveform,
            includeHistogram);
        histogram.Waveform = waveform;
        if (includeHistogram)
        {
            histogram.Normalize();
        }
    }

    private static void AccumulateStats(
        ushort[] data,
        int width,
        int height,
        HistogramData histogram,
        WaveformData? waveform,
        bool includeHistogram)
    {
        var workers = data.Length / 3 >= MinimumParallelPixels
            ? Math.Min(2, height)
            : 1;
        var waveformColumns = new int[width];
        for (var x = 0; x < width; x++)
        {
            waveformColumns[x] = x * WaveformData.ColumnCount / width;
        }
        if (workers == 1)
        {
            AccumulateRows(
                data,
                width,
                0,
                height,
                includeHistogram ? histogram.Red : null,
                includeHistogram ? histogram.Green : null,
                includeHistogram ? histogram.Blue : null,
                includeHistogram ? histogram.Luminance : null,
                waveform,
                waveformColumns);
        }
        else
        {
            var partials = new StatsBuffer[workers];
            Parallel.For(0, workers, worker =>
            {
                var partial = new StatsBuffer(waveform != null);
                partials[worker] = partial;
                AccumulateRows(
                    data,
                    width,
                    height * worker / workers,
                    height * (worker + 1) / workers,
                    includeHistogram ? partial.Red : null,
                    includeHistogram ? partial.Green : null,
                    includeHistogram ? partial.Blue : null,
                    includeHistogram ? partial.Luminance : null,
                    partial.Waveform,
                    waveformColumns);
            });
            foreach (var partial in partials)
            {
                if (includeHistogram)
                {
                    Merge(histogram.Red, partial.Red);
                    Merge(histogram.Green, partial.Green);
                    Merge(histogram.Blue, partial.Blue);
                    Merge(histogram.Luminance, partial.Luminance);
                }
                if (waveform != null)
                {
                    Merge(waveform.Luminance, partial.Waveform!.Luminance);
                }
            }
        }

        if (waveform == null)
        {
            return;
        }
        foreach (var column in waveformColumns)
        {
            waveform.ColumnSampleCounts[column] = unchecked(
                (ushort)(waveform.ColumnSampleCounts[column] + height));
        }

        if (width < WaveformData.ColumnCount)
        {
            BackFillWaveformColumns(waveform);
        }
    }

    private static void AccumulateRows(
        ushort[] data,
        int width,
        int startY,
        int endY,
        int[]? red,
        int[]? green,
        int[]? blue,
        int[]? luminanceBins,
        WaveformData? waveform,
        int[] waveformColumns)
    {
        for (var y = startY; y < endY; y++)
        {
            var rowOffset = y * width * 3;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + x * 3;
                var r = data[offset] >> 8;
                var g = data[offset + 1] >> 8;
                var b = data[offset + 2] >> 8;
                if (red != null)
                {
                    red[r]++;
                    green![g]++;
                    blue![b]++;
                }

                var luminance = (int)(
                    RedLuminance[r] + GreenLuminance[g] + BlueLuminance[b]);
                luminance = Math.Clamp(luminance, 0, 255);
                if (luminanceBins != null)
                {
                    luminanceBins[luminance]++;
                }
                if (waveform != null)
                {
                    var column = waveformColumns[x];
                    var level = WaveformAccumulator.ToLevel(luminance);
                    waveform.Luminance[
                        level * WaveformData.ColumnCount + column]++;
                }
            }
        }
    }

    private static void AccumulatePreviewRows(
        byte[] bgra,
        int width,
        int startY,
        int endY,
        int[]? red,
        int[]? green,
        int[]? blue,
        int[]? luminanceBins,
        WaveformData? waveform,
        int[]? waveformColumns)
    {
        for (var y = startY; y < endY; y++)
        {
            var rowOffset = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var offset = rowOffset + x * 4;
                var b = bgra[offset];
                var g = bgra[offset + 1];
                var r = bgra[offset + 2];
                if (red != null)
                {
                    red[r]++;
                    green![g]++;
                    blue![b]++;
                }

                var luminance = Math.Clamp(
                    (int)(RedLuminance[r] + GreenLuminance[g] +
                        BlueLuminance[b]),
                    0,
                    255);
                if (luminanceBins != null)
                {
                    luminanceBins[luminance]++;
                }
                if (waveform != null)
                {
                    var column = waveformColumns![x];
                    waveform.Luminance[
                        WaveformAccumulator.ToLevel(luminance) *
                        WaveformData.ColumnCount + column]++;
                    waveform.ColumnSampleCounts[column]++;
                }
            }
        }
    }

    private static void Merge(int[] destination, int[] source)
    {
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] += source[index];
        }
    }

    private static void Merge(ushort[] destination, ushort[] source)
    {
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = unchecked(
                (ushort)(destination[index] + source[index]));
        }
    }

    private static double[] CreateLuminanceTable(double coefficient)
    {
        var values = new double[256];
        for (var value = 0; value < values.Length; value++)
        {
            values[value] = coefficient * value;
        }
        return values;
    }

    private sealed class StatsBuffer
    {
        public StatsBuffer(bool includeWaveform) =>
            Waveform = includeWaveform ? new WaveformData() : null;

        public int[] Red { get; } = new int[256];
        public int[] Green { get; } = new int[256];
        public int[] Blue { get; } = new int[256];
        public int[] Luminance { get; } = new int[256];
        public WaveformData? Waveform { get; }
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

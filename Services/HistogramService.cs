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
    internal const int LibraryHistogramDimension = 150;
    private const int MinimumParallelPixels = 512 * 512;
    private static readonly double[] RedLuminance = CreateLuminanceTable(0.299);
    private static readonly double[] GreenLuminance = CreateLuminanceTable(0.587);
    private static readonly double[] BlueLuminance = CreateLuminanceTable(0.114);

    internal static void CalculatePreviewHistogram(
        byte[] bgra,
        int width,
        int height,
        HistogramData histogram,
        bool includeWaveform)
    {
        CalculateBgraHistogram(
            bgra,
            width,
            height,
            histogram,
            includeWaveform);
    }

    private static void CalculateBgraHistogram(
        byte[] bgra,
        int width,
        int height,
        HistogramData histogram,
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

        var columns = includeWaveform ? new int[width] : [];
        for (var x = 0; x < columns.Length; x++)
        {
            columns[x] = x * WaveformData.ColumnCount / width;
        }

        var workers = bgra.Length / 4 >= MinimumParallelPixels
            ? Math.Min(2, height)
            : 1;
        var partials = new StatsBuffer[workers];
        for (var worker = 0; worker < workers; worker++)
        {
            partials[worker] = new StatsBuffer(includeWaveform);
        }
        Parallel.For(0, workers, worker =>
        {
            partials[worker].Accumulate(
                bgra,
                width,
                height * worker / workers,
                height * (worker + 1) / workers,
                columns);
        });

        var totals = new StatsBuffer(histogram, includeWaveform);
        foreach (var partial in partials)
        {
            totals.MergeFrom(partial);
        }

        totals.BackFillWaveformColumns(width);
        histogram.Normalize();
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

    public HistogramData CalculateHistogram(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var histogram = new HistogramData();
        var data = BitmapConversionService.CopyBgraPixels(bitmap);
        CalculateBgraHistogram(
            data,
            bitmap.PixelSize.Width,
            bitmap.PixelSize.Height,
            histogram,
            includeWaveform: false);
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
        return BitmapConversionService.ConvertToBitmap(image) ??
            throw new InvalidOperationException(
                "Unable to create the Library histogram snapshot.");
    }

    private sealed class StatsBuffer
    {
        private readonly int[] _red;
        private readonly int[] _green;
        private readonly int[] _blue;
        private readonly int[] _luminance;
        private readonly WaveformData? _waveform;

        public StatsBuffer(bool includeWaveform)
            : this(
                new int[256],
                new int[256],
                new int[256],
                new int[256],
                includeWaveform)
        {
        }

        public StatsBuffer(HistogramData histogram, bool includeWaveform)
            : this(
                histogram.Red,
                histogram.Green,
                histogram.Blue,
                histogram.Luminance,
                includeWaveform)
        {
            histogram.Waveform = _waveform;
        }

        private StatsBuffer(
            int[] red,
            int[] green,
            int[] blue,
            int[] luminance,
            bool includeWaveform)
        {
            _red = red;
            _green = green;
            _blue = blue;
            _luminance = luminance;
            _waveform = includeWaveform ? new WaveformData() : null;
        }

        public void Accumulate(
            byte[] bgra,
            int width,
            int startY,
            int endY,
            int[] waveformColumns)
        {
            for (var y = startY; y < endY; y++)
            {
                var rowOffset = y * width * 4;
                for (var x = 0; x < width; x++)
                {
                    var offset = rowOffset + x * 4;
                    var blue = bgra[offset];
                    var green = bgra[offset + 1];
                    var red = bgra[offset + 2];
                    _red[red]++;
                    _green[green]++;
                    _blue[blue]++;

                    var luminance = Math.Clamp(
                        (int)(RedLuminance[red] + GreenLuminance[green] +
                            BlueLuminance[blue]),
                        0,
                        255);
                    _luminance[luminance]++;
                    if (_waveform is not { } waveform)
                    {
                        continue;
                    }

                    var column = waveformColumns[x];
                    waveform.Luminance[
                        WaveformAccumulator.ToLevel(luminance) *
                        WaveformData.ColumnCount + column]++;
                    waveform.ColumnSampleCounts[column]++;
                }
            }
        }

        public void MergeFrom(StatsBuffer source)
        {
            for (var index = 0; index < _red.Length; index++)
            {
                _red[index] += source._red[index];
                _green[index] += source._green[index];
                _blue[index] += source._blue[index];
                _luminance[index] += source._luminance[index];
            }

            if (_waveform is not { } waveform ||
                source._waveform is not { } sourceWaveform)
            {
                return;
            }
            for (var index = 0; index < waveform.Luminance.Length; index++)
            {
                waveform.Luminance[index] = unchecked(
                    (ushort)(waveform.Luminance[index] +
                        sourceWaveform.Luminance[index]));
            }
            for (var index = 0;
                 index < waveform.ColumnSampleCounts.Length;
                 index++)
            {
                waveform.ColumnSampleCounts[index] = unchecked(
                    (ushort)(waveform.ColumnSampleCounts[index] +
                        sourceWaveform.ColumnSampleCounts[index]));
            }
        }

        public void BackFillWaveformColumns(int width)
        {
            if (width >= WaveformData.ColumnCount ||
                _waveform is not { } waveform)
            {
                return;
            }

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
                    waveform.Luminance[
                        level * WaveformData.ColumnCount + column] =
                        waveform.Luminance[
                            level * WaveformData.ColumnCount + sourceColumn];
                }
            }
        }
    }
}

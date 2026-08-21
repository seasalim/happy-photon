using System.Runtime.CompilerServices;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class ToneLutApplicator
{
    public static void Apply(MagickImage image, double[] lut)
    {
        ApplyCore(image, null, new ToneLuts(lut, lut, lut));
    }

    public static void Apply(MagickImage image, ToneLuts luts)
    {
        ApplyCore(image, null, luts);
    }

    internal static void Apply(
        MagickImage image,
        double[,] matrix,
        double[] lut)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.GetLength(0) != 3 || matrix.GetLength(1) != 3)
        {
            throw new ArgumentException("Expected a 3x3 RGB matrix.", nameof(matrix));
        }
        ApplyCore(image, matrix, new ToneLuts(lut, lut, lut));
    }

    internal static void Apply(
        MagickImage image,
        double[,] matrix,
        ToneLuts luts)
    {
        ValidateMatrix(matrix);
        ApplyCore(image, matrix, luts);
    }

    internal static void ApplyResting(
        MagickImage image,
        double[,] matrix,
        ToneLuts luts,
        RenderExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(luts);
        if (matrix.GetLength(0) != 3 || matrix.GetLength(1) != 3)
        {
            throw new ArgumentException("Expected a 3x3 RGB matrix.", nameof(matrix));
        }

        execution.ThrowIfCancellationRequested();
        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException("Unable to access Q16 pixels.");
        execution.ThrowIfCancellationRequested();
        var channels = pixels.Channels;
        var red = GetChannelIndex(pixels, PixelChannel.Red);
        var green = GetChannelIndex(pixels, PixelChannel.Green);
        var blue = GetChannelIndex(pixels, PixelChannel.Blue);
        var pixelCount = checked((int)(image.Width * image.Height));

        var workers = WorkerCount(pixelCount);
        Parallel.For(
            0,
            workers,
            execution.ParallelOptions,
            worker =>
            {
                var (start, end) = ChunkRange(pixelCount, worker, workers);
                for (var pixel = start; pixel < end; pixel++)
                {
                    if ((pixel & CancellationCheckMask) == 0)
                    {
                        execution.ThrowIfCancellationRequested();
                    }
                    var offset = pixel * channels;
                    var r = values[offset + red] / (double)ushort.MaxValue;
                    var g = values[offset + green] / (double)ushort.MaxValue;
                    var b = values[offset + blue] / (double)ushort.MaxValue;
                    values[offset + red] = ToQuantum(Interpolate(
                        luts.Red, Transform(matrix, 0, r, g, b)));
                    values[offset + green] = ToQuantum(Interpolate(
                        luts.Green, Transform(matrix, 1, r, g, b)));
                    values[offset + blue] = ToQuantum(Interpolate(
                        luts.Blue, Transform(matrix, 2, r, g, b)));
                }
            });
        execution.ThrowIfCancellationRequested();
        pixels.SetArea(0, 0, image.Width, image.Height, values);
        execution.ThrowIfCancellationRequested();
    }

    internal static void ApplyResting(
        MagickImage image,
        double[] lut,
        RenderExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(lut);
        if (lut.Length != ToneLut.Length)
        {
            throw new ArgumentException(
                $"Expected a {ToneLut.Length}-entry LUT.",
                nameof(lut));
        }

        execution.ThrowIfCancellationRequested();
        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException("Unable to access Q16 pixels.");
        execution.ThrowIfCancellationRequested();
        var channels = pixels.Channels;
        var red = GetChannelIndex(pixels, PixelChannel.Red);
        var green = GetChannelIndex(pixels, PixelChannel.Green);
        var blue = GetChannelIndex(pixels, PixelChannel.Blue);
        var pixelCount = checked((int)(image.Width * image.Height));

        var workers = WorkerCount(pixelCount);
        Parallel.For(
            0,
            workers,
            execution.ParallelOptions,
            worker =>
            {
                var (start, end) = ChunkRange(pixelCount, worker, workers);
                for (var pixel = start; pixel < end; pixel++)
                {
                    if ((pixel & CancellationCheckMask) == 0)
                    {
                        execution.ThrowIfCancellationRequested();
                    }
                    var offset = pixel * channels;
                    values[offset + red] = ToQuantum(Interpolate(
                        lut, values[offset + red] / (double)ushort.MaxValue));
                    values[offset + green] = ToQuantum(Interpolate(
                        lut, values[offset + green] / (double)ushort.MaxValue));
                    values[offset + blue] = ToQuantum(Interpolate(
                        lut, values[offset + blue] / (double)ushort.MaxValue));
                }
            });
        execution.ThrowIfCancellationRequested();
        pixels.SetArea(0, 0, image.Width, image.Height, values);
        execution.ThrowIfCancellationRequested();
    }

    private static void ApplyCore(
        MagickImage image,
        double[,]? matrix,
        ToneLuts luts)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(luts);
        ValidateLut(luts.Red);
        ValidateLut(luts.Green);
        ValidateLut(luts.Blue);

        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException("Unable to access Q16 pixels.");
        var channels = pixels.Channels;
        var red = GetChannelIndex(pixels, PixelChannel.Red);
        var green = GetChannelIndex(pixels, PixelChannel.Green);
        var blue = GetChannelIndex(pixels, PixelChannel.Blue);
        var pixelCount = checked((int)(image.Width * image.Height));

        var workers = WorkerCount(pixelCount);
        Parallel.For(0, workers, worker =>
        {
            var (start, end) = ChunkRange(pixelCount, worker, workers);
            if (matrix == null)
            {
                for (var pixel = start; pixel < end; pixel++)
                {
                    var offset = pixel * channels;
                    values[offset + red] = ToQuantum(Interpolate(
                        luts.Red, values[offset + red] / (double)ushort.MaxValue));
                    values[offset + green] = ToQuantum(Interpolate(
                        luts.Green, values[offset + green] / (double)ushort.MaxValue));
                    values[offset + blue] = ToQuantum(Interpolate(
                        luts.Blue, values[offset + blue] / (double)ushort.MaxValue));
                }
                return;
            }

            for (var pixel = start; pixel < end; pixel++)
            {
                var offset = pixel * channels;
                var r = values[offset + red] / (double)ushort.MaxValue;
                var g = values[offset + green] / (double)ushort.MaxValue;
                var b = values[offset + blue] / (double)ushort.MaxValue;
                values[offset + red] = ToQuantum(Interpolate(
                    luts.Red, Transform(matrix, 0, r, g, b)));
                values[offset + green] = ToQuantum(Interpolate(
                    luts.Green, Transform(matrix, 1, r, g, b)));
                values[offset + blue] = ToQuantum(Interpolate(
                    luts.Blue, Transform(matrix, 2, r, g, b)));
            }
        });
        pixels.SetArea(0, 0, image.Width, image.Height, values);
    }

    // Chunked partitioning matches AgxCrossing: each pixel is independent, so
    // output is bit-identical for any worker count or cap.
    private const int CancellationCheckMask = 0x1FFF;

    private static int WorkerCount(int pixelCount) =>
        Math.Min(Environment.ProcessorCount, Math.Max(1, pixelCount / 8192));

    private static (int Start, int End) ChunkRange(
        int pixelCount,
        int worker,
        int workers) =>
        ((int)((long)pixelCount * worker / workers),
            (int)((long)pixelCount * (worker + 1) / workers));

    private static void ValidateMatrix(double[,] matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.GetLength(0) != 3 || matrix.GetLength(1) != 3)
        {
            throw new ArgumentException("Expected a 3x3 RGB matrix.", nameof(matrix));
        }
    }

    private static void ValidateLut(double[] lut)
    {
        ArgumentNullException.ThrowIfNull(lut);
        if (lut.Length != ToneLut.Length)
        {
            throw new ArgumentException(
                $"Expected a {ToneLut.Length}-entry LUT.",
                nameof(lut));
        }
    }

    private static double Transform(
        double[,] matrix,
        int row,
        double red,
        double green,
        double blue) =>
        matrix[row, 0] * red +
            matrix[row, 1] * green +
            matrix[row, 2] * blue;

    private static int GetChannelIndex(
        IPixelCollection<ushort> pixels,
        PixelChannel channel) =>
        checked((int)(pixels.GetChannelIndex(channel) ??
            throw new InvalidOperationException(
                $"The image has no {channel} channel.")));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double Interpolate(double[] lut, double sample)
    {
        var position = Math.Clamp(sample, 0, 1) * (ToneLut.Length - 1);
        var lower = (int)position;
        if (lower >= lut.Length - 1)
        {
            return lut[^1];
        }

        var fraction = position - lower;
        return lut[lower] + (lut[lower + 1] - lut[lower]) * fraction;
    }

    private static ushort ToQuantum(double value) =>
        (ushort)Math.Round(
            Math.Clamp(value, 0, 1) * ushort.MaxValue,
            MidpointRounding.AwayFromZero);
}

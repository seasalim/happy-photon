using System.Runtime.CompilerServices;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class ToneLutApplicator
{
    public static void Apply(MagickImage image, double[] lut)
    {
        ApplyCore(image, null, lut);
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
        ApplyCore(image, matrix, lut);
    }

    private static void ApplyCore(
        MagickImage image,
        double[,]? matrix,
        double[] lut)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(lut);
        if (lut.Length != ToneLut.Length)
        {
            throw new ArgumentException(
                $"Expected a {ToneLut.Length}-entry LUT.",
                nameof(lut));
        }

        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException("Unable to access Q16 pixels.");
        var channels = pixels.Channels;
        var red = GetChannelIndex(pixels, PixelChannel.Red);
        var green = GetChannelIndex(pixels, PixelChannel.Green);
        var blue = GetChannelIndex(pixels, PixelChannel.Blue);
        var pixelCount = checked((int)(image.Width * image.Height));

        Parallel.For(0, pixelCount, pixel =>
        {
            var offset = pixel * channels;
            if (matrix == null)
            {
                values[offset + red] = ToQuantum(Interpolate(
                    lut, values[offset + red] / (double)ushort.MaxValue));
                values[offset + green] = ToQuantum(Interpolate(
                    lut, values[offset + green] / (double)ushort.MaxValue));
                values[offset + blue] = ToQuantum(Interpolate(
                    lut, values[offset + blue] / (double)ushort.MaxValue));
                return;
            }

            var r = values[offset + red] / (double)ushort.MaxValue;
            var g = values[offset + green] / (double)ushort.MaxValue;
            var b = values[offset + blue] / (double)ushort.MaxValue;
            values[offset + red] = ToQuantum(Interpolate(
                lut, Transform(matrix, 0, r, g, b)));
            values[offset + green] = ToQuantum(Interpolate(
                lut, Transform(matrix, 1, r, g, b)));
            values[offset + blue] = ToQuantum(Interpolate(
                lut, Transform(matrix, 2, r, g, b)));
        });
        pixels.SetArea(0, 0, image.Width, image.Height, values);
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

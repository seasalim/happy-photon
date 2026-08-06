using ImageMagick;

namespace HappyPhoton.Services;

internal static class ToneLutApplicator
{
    public static void Apply(MagickImage image, ushort[] lut)
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
            values[offset + red] = Interpolate(lut, values[offset + red]);
            values[offset + green] = Interpolate(lut, values[offset + green]);
            values[offset + blue] = Interpolate(lut, values[offset + blue]);
        });
        pixels.SetArea(0, 0, image.Width, image.Height, values);
    }

    private static int GetChannelIndex(
        IPixelCollection<ushort> pixels,
        PixelChannel channel) =>
        checked((int)(pixels.GetChannelIndex(channel) ??
            throw new InvalidOperationException(
                $"The image has no {channel} channel.")));

    internal static ushort Interpolate(ushort[] lut, ushort sample)
    {
        var scaled = (uint)sample * (ToneLut.Length - 1);
        var lower = (int)(scaled / ushort.MaxValue);
        if (lower >= lut.Length - 1)
        {
            return lut[^1];
        }

        var remainder = scaled % ushort.MaxValue;
        var numerator =
            (ulong)lut[lower] * (ushort.MaxValue - remainder) +
            (ulong)lut[lower + 1] * remainder;
        return (ushort)((numerator + ushort.MaxValue / 2) / ushort.MaxValue);
    }
}

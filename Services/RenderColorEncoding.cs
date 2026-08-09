using ImageMagick;

namespace HappyPhoton.Services;

internal static class RenderColorEncoding
{
    private const int LutSize = 4096;

    private static readonly Lazy<ushort[]> SrgbDecodeLut =
        new(() => ComposeLut(DecodeSrgb));

    private static readonly Lazy<ushort[]> SrgbEncodeLut =
        new(() => ComposeLut(EncodeSrgb));

    public static void ResizeInLinearLight(MagickImage image, int maxDimension)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (maxDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDimension));
        }

        if (image.Width <= (uint)maxDimension &&
            image.Height <= (uint)maxDimension)
        {
            return;
        }

        ToneLutApplicator.Apply(image, SrgbDecodeLut.Value);
        BitmapConversionService.ResizeToMaxDimension(image, maxDimension);
        ToneLutApplicator.Apply(image, SrgbEncodeLut.Value);
    }

    public static void RetagAsSrgb(MagickImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        image.SetAttribute("colorspace", "sRGB");
        if (image.ColorSpace != ColorSpace.sRGB)
        {
            throw new InvalidOperationException(
                "Unable to tag display-referred pixels as sRGB.");
        }
    }

    public static void ApplyLut(MagickImage image, ushort[] values)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != LutSize)
        {
            throw new ArgumentException(
                $"Expected a {LutSize}-entry LUT.",
                nameof(values));
        }

        var samples = new ushort[LutSize * 3];
        for (var i = 0; i < values.Length; i++)
        {
            var value = values[i];
            var offset = i * 3;
            samples[offset] = value;
            samples[offset + 1] = value;
            samples[offset + 2] = value;
        }

        using var lut = new MagickImage(
            MagickColors.Black,
            LutSize,
            1);
        var settings = new PixelImportSettings(
            LutSize,
            1,
            StorageType.Short,
            PixelMapping.RGB);
        lut.ImportPixels(
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                samples.AsSpan()),
            settings);
        image.Clut(lut, PixelInterpolateMethod.Bilinear, Channels.RGB);
    }

    private static ushort[] ComposeLut(Func<double, double> transform)
    {
        var result = new ushort[LutSize];
        for (var i = 0; i < result.Length; i++)
        {
            var value = transform((double)i / (LutSize - 1));
            result[i] = (ushort)Math.Round(
                Math.Clamp(value, 0, 1) * Quantum.Max);
        }

        return result;
    }

    private static double DecodeSrgb(double value) =>
        value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double EncodeSrgb(double value) =>
        value <= 0.0031308
            ? 12.92 * value
            : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
}

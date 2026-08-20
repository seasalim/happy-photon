using System.Runtime.CompilerServices;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class RenderColorEncoding
{
    private static readonly Lazy<double[]> SrgbDecodeLut =
        new(() => ComposeLut(DecodeSrgb));

    private static readonly Lazy<double[]> SrgbEncodeLut =
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

    internal static void ConvertEncodedRec2020ToTarget(
        MagickImage image,
        OutputColorSpace outputColorSpace)
    {
        ArgumentNullException.ThrowIfNull(image);
        var matrix = outputColorSpace switch
        {
            OutputColorSpace.Srgb =>
                RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb,
            OutputColorSpace.DisplayP3 =>
                RgbColorSpaceMatrices.LinearRec2020ToLinearDisplayP3,
            _ => throw new ArgumentOutOfRangeException(
                nameof(outputColorSpace), outputColorSpace, null)
        };

        using var pixels = image.GetPixels();
        var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException("Unable to access Q16 pixels.");
        var layout = RenderKernelSupport.GetLayout(pixels);
        var pixelCount = checked((int)(image.Width * image.Height));
        var decode = SrgbDecodeLut.Value;
        var encode = SrgbEncodeLut.Value;
        var m00 = matrix[0, 0];
        var m01 = matrix[0, 1];
        var m02 = matrix[0, 2];
        var m10 = matrix[1, 0];
        var m11 = matrix[1, 1];
        var m12 = matrix[1, 2];
        var m20 = matrix[2, 0];
        var m21 = matrix[2, 1];
        var m22 = matrix[2, 2];
        var workers = Math.Min(
            Environment.ProcessorCount,
            Math.Max(1, (pixelCount + 32_767) / 32_768));
        Parallel.For(0, workers, worker =>
        {
            var start = pixelCount * worker / workers;
            var end = pixelCount * (worker + 1) / workers;
            for (var pixel = start; pixel < end; pixel++)
            {
                var offset = pixel * layout.Channels;
                var red = decode[values[offset + layout.Red]];
                var green = decode[values[offset + layout.Green]];
                var blue = decode[values[offset + layout.Blue]];
                values[offset + layout.Red] = EncodeQuantum(
                    m00 * red + m01 * green + m02 * blue,
                    encode);
                values[offset + layout.Green] = EncodeQuantum(
                    m10 * red + m11 * green + m12 * blue,
                    encode);
                values[offset + layout.Blue] = EncodeQuantum(
                    m20 * red + m21 * green + m22 * blue,
                    encode);
            }
        });
        pixels.SetArea(0, 0, image.Width, image.Height, values);
        RetagAsSrgb(image);
    }

    private static double[] ComposeLut(Func<double, double> transform)
    {
        var result = new double[ToneLut.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = transform((double)i / (result.Length - 1));
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort EncodeQuantum(double value, double[] encode)
    {
        if (value <= 0)
        {
            return ushort.MinValue;
        }
        if (value >= 1)
        {
            return ushort.MaxValue;
        }

        var position = value * ushort.MaxValue;
        var lower = (int)position;
        var fraction = position - lower;
        var encoded = encode[lower] +
            (encode[lower + 1] - encode[lower]) * fraction;
        return (ushort)(encoded * ushort.MaxValue + 0.5);
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

using System.Runtime.CompilerServices;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class WorkingSpaceColorConversion
{
    private static readonly Lazy<ushort[]> SrgbDecodeTable = new(CreateSrgbDecodeTable);

    internal static unsafe void ConvertSrgbToLinearRec2020(MagickImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        image.SetAttribute("colorspace", "sRGB");
        // Materialize direct, writable storage before bypassing Magick's copy-on-write with a pointer.
        image.CopyPixels(image, new MagickGeometry(0, 0, 1, 1));
        using (var pixels = image.GetPixelsUnsafe())
        {
            var layout = RenderKernelSupport.GetLayout(pixels);
            var values = (ushort*)pixels.GetAreaPointer(0, 0, image.Width, image.Height);
            if (values == null) throw new InvalidOperationException("Unable to access Q16 pixels.");
            var count = checked((int)(image.Width * image.Height));
            var sampleCount = checked(count * layout.Channels);
            var decode = SrgbDecodeTable.Value;
            var matrix = RgbColorSpaceMatrices.LinearSrgbToLinearRec2020;
            var m00 = matrix[0, 0]; var m01 = matrix[0, 1]; var m02 = matrix[0, 2];
            var m10 = matrix[1, 0]; var m11 = matrix[1, 1]; var m12 = matrix[1, 2];
            var m20 = matrix[2, 0]; var m21 = matrix[2, 1]; var m22 = matrix[2, 2];
            var workers = Math.Min(Environment.ProcessorCount, Math.Max(1, (count + 262_143) / 262_144));
            Parallel.For(0, workers, ConvertWorker);
            // A separate view commits disk-backed caches without invalidating the source pointer.
            using var destination = image.GetPixelsUnsafe();
            destination.SetArea(0, 0, image.Width, image.Height,
                new ReadOnlySpan<ushort>(values, sampleCount));

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            void ConvertWorker(int worker)
            {
                var end = (int)((long)count * (worker + 1) / workers);
                for (var pixel = (int)((long)count * worker / workers); pixel < end; pixel++)
                {
                    var offset = pixel * layout.Channels;
                    double red = decode[values[offset + layout.Red]];
                    double green = decode[values[offset + layout.Green]];
                    double blue = decode[values[offset + layout.Blue]];
                    values[offset + layout.Red] = ToQuantum(m00 * red + m01 * green + m02 * blue);
                    values[offset + layout.Green] = ToQuantum(m10 * red + m11 * green + m12 * blue);
                    values[offset + layout.Blue] = ToQuantum(m20 * red + m21 * green + m22 * blue);
                }
            }
        }
        image.SetAttribute("colorspace", "RGB");
        if (image.ColorSpace != ColorSpace.RGB)
        {
            throw new InvalidOperationException(
                "Working-space conversion did not produce linear RGB samples.");
        }
    }

    private static ushort ToQuantum(double value) => value <= 0 ? (ushort)0 :
        value >= ushort.MaxValue ? ushort.MaxValue : (ushort)(value + 0.5);

    private static ushort[] CreateSrgbDecodeTable()
    {
        // Preserve the native EOTF approximation and its first Q16 rounding on each platform.
        using var ramp = new MagickImage(MagickColors.Black, 256, 256) { ColorType = ColorType.TrueColor };
        using (var pixels = ramp.GetPixels())
        {
            var values = new ushort[65536 * 3];
            for (var i = 0; i < 65536; i++)
                values[i * 3] = values[i * 3 + 1] = values[i * 3 + 2] = (ushort)i;
            pixels.SetArea(0, 0, ramp.Width, ramp.Height, values);
        }
        ramp.SetAttribute("colorspace", "sRGB");
        ramp.ColorSpace = ColorSpace.RGB;
        using var linear = ramp.GetPixels();
        var rgb = linear.ToShortArray(PixelMapping.RGB)!;
        return Enumerable.Range(0, 65536).Select(i => rgb[i * 3]).ToArray();
    }
}

using System.Buffers;
using System.Runtime.CompilerServices;
using ImageMagick;

namespace HappyPhoton.Services;

internal static partial class DcpHueSatRenderer
{
    private static readonly DcpRenderMatrix WorkingToProPhoto = CreateWorkingToProPhoto();
    private static readonly DcpRenderMatrix ProPhotoToWorking = CreateProPhotoToWorking();

    /// <summary>
    /// Standalone application for tests and tools. The production render
    /// fuses the LUT pass into the AgX crossing's whole-frame array instead;
    /// this entry uses the same whole-frame read → transform → single-write
    /// shape, which is the only pattern proven safe on Magick's
    /// copy-on-write clones (banded area writes lose bands; authentic
    /// pointer writes mutate the shared source).
    /// </summary>
    internal static void Apply(MagickImage image, DcpHueSatMap? map)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (map == null) return;
        map = map.RgbLut == null ? Prepare(map) : map;

        using var pixels = image.GetPixels();
        var layout = RenderKernelSupport.GetLayout(pixels);
        var values = pixels.GetArea(0, 0, image.Width, image.Height) ??
            throw new InvalidOperationException(
                "Unable to access the working pixels for the profile " +
                "HueSat stage.");
        ApplyValues(
            values,
            checked((int)(image.Width * image.Height)),
            layout.Channels,
            layout.Red,
            layout.Green,
            layout.Blue,
            map);
        pixels.SetArea(0, 0, image.Width, image.Height, values);
    }

    /// <summary>
    /// The LUT pass over an interleaved Q16 frame. Called by the AgX
    /// crossing on its already-materialized working array (fused, no extra
    /// allocation) and by <see cref="Apply(MagickImage, DcpHueSatMap?)"/>.
    /// </summary>
    internal static void ApplyValues(
        ushort[] values,
        int pixelCount,
        int channels,
        int redChannel,
        int greenChannel,
        int blueChannel,
        DcpHueSatMap map)
    {
        var prepared = map.RgbLut == null ? Prepare(map) : map;
        var lut = prepared.RgbLut!;
        var workers = Math.Min(
            Environment.ProcessorCount,
            Math.Max(1, (pixelCount + 32_767) / 32_768));
        Parallel.For(0, workers, worker =>
        {
            var start = pixelCount * worker / workers;
            var end = pixelCount * (worker + 1) / workers;
            for (var pixel = start; pixel < end; pixel++)
            {
                ApplyLut(
                    values,
                    pixel * channels,
                    redChannel,
                    greenChannel,
                    blueChannel,
                    lut);
            }
        });
    }

    internal static DcpHueSatDelta Lookup(
        DcpHueSatMap map,
        double hue,
        double saturation,
        double value)
    {
        ArgumentNullException.ThrowIfNull(map);
        var huePosition = WrapHue(hue) * map.HueDivisions / 360.0;
        var hue0 = (int)Math.Floor(huePosition) % map.HueDivisions;
        var hue1 = (hue0 + 1) % map.HueDivisions;
        var hueFraction = huePosition - Math.Floor(huePosition);
        var saturationPosition = Clamp01(saturation) * (map.SaturationDivisions - 1);
        var saturation0 = (int)Math.Floor(saturationPosition);
        var saturation1 = Math.Min(saturation0 + 1, map.SaturationDivisions - 1);
        var saturationFraction = saturationPosition - saturation0;
        var valuePosition = map.ValueDivisions == 1
            ? 0
            : Clamp01(value) * (map.ValueDivisions - 1);
        var value0 = (int)Math.Floor(valuePosition);
        var value1 = Math.Min(value0 + 1, map.ValueDivisions - 1);
        var valueFraction = valuePosition - value0;

        var first = InterpolateTable(
            map,
            map.Table1,
            hue0,
            hue1,
            hueFraction,
            saturation0,
            saturation1,
            saturationFraction,
            value0,
            value1,
            valueFraction);
        if (map.Table2 == null) return first;
        var second = InterpolateTable(
            map,
            map.Table2,
            hue0,
            hue1,
            hueFraction,
            saturation0,
            saturation1,
            saturationFraction,
            value0,
            value1,
            valueFraction);
        return new DcpHueSatDelta(
            Lerp(first.HueShift, second.HueShift, map.IlluminantWeight),
            Lerp(first.SaturationScale, second.SaturationScale, map.IlluminantWeight),
            Lerp(first.ValueScale, second.ValueScale, map.IlluminantWeight));
    }

    internal static DcpHsv ApplyToHsv(
        DcpHueSatMap map,
        double hue,
        double saturation,
        double value)
    {
        var encodedValue = map.ValueDivisions > 1 && map.EncodeValueAsSrgb
            ? EncodeSrgb(Clamp01(value))
            : Clamp01(value);
        var delta = Lookup(map, hue, saturation, encodedValue);
        var resultHue = WrapHue(hue + delta.HueShift);
        var resultSaturation = Clamp01(saturation * delta.SaturationScale);
        encodedValue = Clamp01(encodedValue * delta.ValueScale);
        var resultValue = map.ValueDivisions > 1 && map.EncodeValueAsSrgb
            ? DecodeSrgb(encodedValue)
            : encodedValue;
        return new DcpHsv(resultHue, resultSaturation, resultValue);
    }

    internal static double[] ConvertWorkingToProPhoto(IReadOnlyList<double> rgb)
    {
        if (rgb.Count != 3) throw new ArgumentException("Expected RGB input.");
        return
        [
            WorkingToProPhoto.Row0(rgb[0], rgb[1], rgb[2]),
            WorkingToProPhoto.Row1(rgb[0], rgb[1], rgb[2]),
            WorkingToProPhoto.Row2(rgb[0], rgb[1], rgb[2])
        ];
    }

    internal static double[] ConvertProPhotoToWorking(IReadOnlyList<double> rgb)
    {
        if (rgb.Count != 3) throw new ArgumentException("Expected RGB input.");
        return
        [
            ProPhotoToWorking.Row0(rgb[0], rgb[1], rgb[2]),
            ProPhotoToWorking.Row1(rgb[0], rgb[1], rgb[2]),
            ProPhotoToWorking.Row2(rgb[0], rgb[1], rgb[2])
        ];
    }

    private static DcpHueSatDelta InterpolateTable(
        DcpHueSatMap map,
        float[] table,
        int hue0,
        int hue1,
        double hueFraction,
        int saturation0,
        int saturation1,
        double saturationFraction,
        int value0,
        int value1,
        double valueFraction)
    {
        var lowValue = Bilinear(
            Read(table, map, hue0, saturation0, value0),
            Read(table, map, hue1, saturation0, value0),
            Read(table, map, hue0, saturation1, value0),
            Read(table, map, hue1, saturation1, value0),
            hueFraction,
            saturationFraction);
        if (value0 == value1) return lowValue;
        var highValue = Bilinear(
            Read(table, map, hue0, saturation0, value1),
            Read(table, map, hue1, saturation0, value1),
            Read(table, map, hue0, saturation1, value1),
            Read(table, map, hue1, saturation1, value1),
            hueFraction,
            saturationFraction);
        return Lerp(lowValue, highValue, valueFraction);
    }

    private static DcpHueSatDelta Read(
        float[] table,
        DcpHueSatMap map,
        int hue,
        int saturation,
        int value)
    {
        var index = checked(
            ((value * map.HueDivisions + hue) *
                map.SaturationDivisions + saturation) * 3);
        return new DcpHueSatDelta(table[index], table[index + 1], table[index + 2]);
    }

    private static DcpHueSatDelta Bilinear(
        DcpHueSatDelta lowLow,
        DcpHueSatDelta highLow,
        DcpHueSatDelta lowHigh,
        DcpHueSatDelta highHigh,
        double horizontal,
        double vertical) => Lerp(
            Lerp(lowLow, highLow, horizontal),
            Lerp(lowHigh, highHigh, horizontal),
            vertical);

    private static DcpHueSatDelta Lerp(
        DcpHueSatDelta first,
        DcpHueSatDelta second,
        double weight) => new(
            Lerp(first.HueShift, second.HueShift, weight),
            Lerp(first.SaturationScale, second.SaturationScale, weight),
            Lerp(first.ValueScale, second.ValueScale, weight));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Lerp(double first, double second, double weight) =>
        first * (1 - weight) + second * weight;

    private static void RgbToHsv(
        double red,
        double green,
        double blue,
        out double hue,
        out double saturation,
        out double value)
    {
        value = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = value - minimum;
        saturation = value <= 0 ? 0 : delta / value;
        if (delta <= 1e-15)
        {
            hue = 0;
            return;
        }
        if (value == red) hue = 60 * ((green - blue) / delta);
        else if (value == green) hue = 60 * (2 + (blue - red) / delta);
        else hue = 60 * (4 + (red - green) / delta);
        hue = WrapHue(hue);
    }

    private static void HsvToRgb(
        double hue,
        double saturation,
        double value,
        out double red,
        out double green,
        out double blue)
    {
        var chroma = value * saturation;
        var sector = WrapHue(hue) / 60;
        var intermediate = chroma * (1 - Math.Abs(sector % 2 - 1));
        (red, green, blue) = sector switch
        {
            < 1 => (chroma, intermediate, 0d),
            < 2 => (intermediate, chroma, 0d),
            < 3 => (0d, chroma, intermediate),
            < 4 => (0d, intermediate, chroma),
            < 5 => (intermediate, 0d, chroma),
            _ => (chroma, 0d, intermediate)
        };
        var match = value - chroma;
        red += match;
        green += match;
        blue += match;
    }

    private static double EncodeSrgb(double value) => value <= 0.0031308
        ? 12.92 * value
        : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;

    private static double DecodeSrgb(double value) => value <= 0.04045
        ? value / 12.92
        : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double WrapHue(double hue)
    {
        hue %= 360;
        return hue < 0 ? hue + 360 : hue;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static ushort EncodeQuantum(double value) =>
        (ushort)(Clamp01(value) * ushort.MaxValue + 0.5);

    private static DcpRenderMatrix CreateWorkingToProPhoto()
    {
        var d65ToD50 = ChromaticAdaptation.CreateBradfordMatrix(
            [0.95047, 1.0, 1.08883],
            [0.96422, 1.0, 0.82521]);
        var proPhotoToXyzD50 = ProPhotoToXyzD50();
        return new DcpRenderMatrix(ChromaticAdaptation.Multiply(
            DcpMatrixCalculator.Invert(proPhotoToXyzD50),
            ChromaticAdaptation.Multiply(
                d65ToD50,
                RgbColorSpaceMatrices.LinearRec2020ToXyzD65DerivedExact)));
    }

    private static DcpRenderMatrix CreateProPhotoToWorking()
    {
        var d50ToD65 = ChromaticAdaptation.CreateBradfordMatrix(
            [0.96422, 1.0, 0.82521],
            [0.95047, 1.0, 1.08883]);
        return new DcpRenderMatrix(ChromaticAdaptation.Multiply(
            RgbColorSpaceMatrices.XyzD65ToLinearRec2020DerivedExact,
            ChromaticAdaptation.Multiply(d50ToD65, ProPhotoToXyzD50())));
    }

    private static double[,] ProPhotoToXyzD50() => new[,]
    {
        { 0.7976749, 0.1351917, 0.0313534 },
        { 0.2880402, 0.7118741, 0.0000857 },
        { 0.0, 0.0, 0.82521 }
    };
}

internal readonly record struct DcpHueSatDelta(
    double HueShift,
    double SaturationScale,
    double ValueScale);

internal readonly record struct DcpHsv(
    double Hue,
    double Saturation,
    double Value);

internal readonly record struct DcpRenderMatrix(
    double M00,
    double M01,
    double M02,
    double M10,
    double M11,
    double M12,
    double M20,
    double M21,
    double M22)
{
    internal DcpRenderMatrix(double[,] values) : this(
        values[0, 0], values[0, 1], values[0, 2],
        values[1, 0], values[1, 1], values[1, 2],
        values[2, 0], values[2, 1], values[2, 2])
    {
        if (values.GetLength(0) != 3 || values.GetLength(1) != 3)
        {
            throw new ArgumentException("Expected a 3x3 matrix.", nameof(values));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal double Row0(double red, double green, double blue) =>
        M00 * red + M01 * green + M02 * blue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal double Row1(double red, double green, double blue) =>
        M10 * red + M11 * green + M12 * blue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal double Row2(double red, double green, double blue) =>
        M20 * red + M21 * green + M22 * blue;
}

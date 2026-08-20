using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

internal static partial class DcpHueSatRenderer
{
    private const int LutDivisions = 65;
    private const int LutMaximumIndex = LutDivisions - 1;
    private const double LutToUnit = 1.0 / LutMaximumIndex;
    private const double Q10ToUnit = 1.0 / 1023;

    internal static DcpHueSatMap Prepare(DcpHueSatMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.RgbLut != null) return map;
        return map with { RgbLut = BuildRgbLut(map) };
    }

    internal static ushort[] BuildRgbLut(DcpHueSatMap map)
    {
        var lut = GC.AllocateUninitializedArray<ushort>(
            LutDivisions * LutDivisions * LutDivisions * 3);
        Parallel.For(0, LutDivisions, redIndex =>
        {
            var red = redIndex * LutToUnit;
            for (var greenIndex = 0; greenIndex < LutDivisions; greenIndex++)
            for (var blueIndex = 0; blueIndex < LutDivisions; blueIndex++)
            {
                var transformed = TransformWorkingRgb(
                    map,
                    red,
                    greenIndex * LutToUnit,
                    blueIndex * LutToUnit);
                var offset = LutOffset(redIndex, greenIndex, blueIndex);
                lut[offset] = EncodeQuantum(transformed.Red);
                lut[offset + 1] = EncodeQuantum(transformed.Green);
                lut[offset + 2] = EncodeQuantum(transformed.Blue);
            }
        });
        return lut;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyLut(
        ushort[] values,
        int offset,
        int redChannel,
        int greenChannel,
        int blueChannel,
        ushort[] lut)
    {
        var red = values[offset + redChannel];
        var green = values[offset + greenChannel];
        var blue = values[offset + blueChannel];
        var red0 = Math.Min(red >> 10, LutMaximumIndex - 1);
        var green0 = Math.Min(green >> 10, LutMaximumIndex - 1);
        var blue0 = Math.Min(blue >> 10, LutMaximumIndex - 1);
        var redFraction = (red & 1023) * Q10ToUnit;
        var greenFraction = (green & 1023) * Q10ToUnit;
        var blueFraction = (blue & 1023) * Q10ToUnit;

        var c000 = LutOffset(red0, green0, blue0);
        var c100 = LutOffset(red0 + 1, green0, blue0);
        var c010 = LutOffset(red0, green0 + 1, blue0);
        var c110 = LutOffset(red0 + 1, green0 + 1, blue0);
        var c001 = LutOffset(red0, green0, blue0 + 1);
        var c101 = LutOffset(red0 + 1, green0, blue0 + 1);
        var c011 = LutOffset(red0, green0 + 1, blue0 + 1);
        var c111 = LutOffset(red0 + 1, green0 + 1, blue0 + 1);
        values[offset + redChannel] = Trilinear(
            lut, c000, c100, c010, c110, c001, c101, c011, c111,
            redFraction, greenFraction, blueFraction, 0);
        values[offset + greenChannel] = Trilinear(
            lut, c000, c100, c010, c110, c001, c101, c011, c111,
            redFraction, greenFraction, blueFraction, 1);
        values[offset + blueChannel] = Trilinear(
            lut, c000, c100, c010, c110, c001, c101, c011, c111,
            redFraction, greenFraction, blueFraction, 2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort Trilinear(
        ushort[] lut,
        int c000,
        int c100,
        int c010,
        int c110,
        int c001,
        int c101,
        int c011,
        int c111,
        double red,
        double green,
        double blue,
        int channel)
    {
        var low = Lerp(
            Lerp(lut[c000 + channel], lut[c100 + channel], red),
            Lerp(lut[c010 + channel], lut[c110 + channel], red),
            green);
        var high = Lerp(
            Lerp(lut[c001 + channel], lut[c101 + channel], red),
            Lerp(lut[c011 + channel], lut[c111 + channel], red),
            green);
        var value = Lerp(low, high, blue);
        return (ushort)Math.Clamp((int)(value + 0.5), 0, ushort.MaxValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int LutOffset(int red, int green, int blue) =>
        ((red * LutDivisions + green) * LutDivisions + blue) * 3;

    internal static double[] EvaluateLut(
        DcpHueSatMap map,
        IReadOnlyList<double> rgb)
    {
        if (rgb.Count != 3) throw new ArgumentException("Expected RGB input.");
        map = map.RgbLut == null ? Prepare(map) : map;
        var values = rgb.Select(EncodeQuantum).ToArray();
        ApplyLut(values, 0, 0, 1, 2, map.RgbLut!);
        return values.Select(value => value / (double)ushort.MaxValue).ToArray();
    }

    internal static (double Red, double Green, double Blue) TransformWorkingRgb(
        DcpHueSatMap map,
        double red,
        double green,
        double blue)
    {
        var proPhotoRed = Clamp01(WorkingToProPhoto.Row0(red, green, blue));
        var proPhotoGreen = Clamp01(WorkingToProPhoto.Row1(red, green, blue));
        var proPhotoBlue = Clamp01(WorkingToProPhoto.Row2(red, green, blue));
        RgbToHsv(
            proPhotoRed,
            proPhotoGreen,
            proPhotoBlue,
            out var hue,
            out var saturation,
            out var value);
        var transformed = ApplyToHsv(map, hue, saturation, value);
        HsvToRgb(
            transformed.Hue,
            transformed.Saturation,
            transformed.Value,
            out red,
            out green,
            out blue);
        return (
            ProPhotoToWorking.Row0(red, green, blue),
            ProPhotoToWorking.Row1(red, green, blue),
            ProPhotoToWorking.Row2(red, green, blue));
    }
}

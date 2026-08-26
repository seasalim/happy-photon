using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LensCorrectionProcessorTests
{
    private static byte[] SolidRgb(
        int width, int height, ushort red, ushort green, ushort blue)
    {
        var values = new ushort[width * height * 3];
        for (var index = 0; index < values.Length; index += 3)
        {
            values[index] = red;
            values[index + 1] = green;
            values[index + 2] = blue;
        }
        return ToBytes(values);
    }

    private static byte[] GradientRgb(int width, int height)
    {
        var values = new ushort[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        for (var channel = 0; channel < 3; channel++)
            values[(y * width + x) * 3 + channel] =
                (ushort)Math.Round(x / (double)(width - 1) * ushort.MaxValue);
        return ToBytes(values);
    }

    private static byte[] InjectRadialCoordinateField(
        int size,
        IReadOnlyList<double> radial)
    {
        var values = new ushort[size * size * 3];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        for (var channel = 0; channel < 3; channel++)
        {
            var targetX = (x + 0.5) / size;
            var targetY = (y + 0.5) / size;
            var estimateX = targetX;
            var estimateY = targetY;
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var (mappedX, mappedY) = RadialWarp(
                    estimateX, estimateY, radial[channel]);
                estimateX += targetX - mappedX;
                estimateY += targetY - mappedY;
            }
            values[(y * size + x) * 3 + channel] = (ushort)Math.Clamp(
                Math.Round(estimateX * ushort.MaxValue), 0, ushort.MaxValue);
        }
        return ToBytes(values);
    }

    private static byte[] InjectTableCoordinateField(
        int size,
        int logicalSize,
        LensRadialTable distortion,
        LensChromaticAberrationTable ca)
    {
        var values = new ushort[size * size * 3];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        for (var channel = 0; channel < 3; channel++)
        {
            var targetX = (x + 0.5) / size;
            var targetY = (y + 0.5) / size;
            var estimateX = targetX;
            var estimateY = targetY;
            for (var iteration = 0; iteration < 12; iteration++)
            {
                var mapped = TableWarp(
                    estimateX, estimateY, logicalSize, channel, distortion, ca);
                estimateX += targetX - mapped.X;
                estimateY += targetY - mapped.Y;
            }
            values[(y * size + x) * 3 + channel] = (ushort)Math.Clamp(
                Math.Round(estimateX * ushort.MaxValue), 0, ushort.MaxValue);
        }
        return ToBytes(values);
    }

    private static (double X, double Y) TableWarp(
        double x, double y, int size, int channel,
        LensRadialTable distortion,
        LensChromaticAberrationTable ca)
    {
        var dx = (x - 0.5) * (size - 1);
        var dy = (y - 0.5) * (size - 1);
        var radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius == 0) return (x, y);
        var normalized = radius /
            (FujiNativePixelsPerTableRadiusUnit *
             distortion.Scale * distortion.Values.Count);
        var offset = radius *
            Linear(distortion.Radii, distortion.Values, normalized) /
            45;
        if (channel != 1)
        {
            var channelValues = channel == 0 ? ca.Red : ca.Blue;
            offset += radius * Linear(ca.Radii, channelValues, normalized);
        }
        var factor = (radius + offset) / radius;
        return (0.5 + dx * factor / (size - 1),
            0.5 + dy * factor / (size - 1));
    }

    private static void AssertNativeBoundaryIsCovered(
        int nativeSize,
        LensPrescription prescription)
    {
        var plan = new LensCorrectionPlan(
            nativeSize, nativeSize, nativeSize, nativeSize,
            1, prescription, BaseDecodeSettings.Default, zoom: 1,
            new LensCorrectionReferenceFrame(
                nativeSize, nativeSize, nativeSize, nativeSize));
        var boundary = plan.MapShared(nativeSize - 1, nativeSize / 2);
        Assert.True(double.IsFinite(boundary.X));
        Assert.True(double.IsFinite(boundary.Y));
        Assert.InRange(boundary.X, 0, nativeSize - 1d);
        Assert.InRange(boundary.Y, 0, nativeSize - 1d);
    }

    private static double Linear(
        IReadOnlyList<double> radii,
        IReadOnlyList<double> values,
        double radius)
    {
        for (var index = 1; index < radii.Count; index++)
        {
            if (radius > radii[index]) continue;
            var fraction = (radius - radii[index - 1]) /
                (radii[index] - radii[index - 1]);
            return values[index - 1] +
                (values[index] - values[index - 1]) * fraction;
        }
        return values[^1];
    }

    private static (double X, double Y) RadialWarp(
        double x,
        double y,
        double kr1)
    {
        const double maximum = 0.7071067811865476;
        var dx = (x - 0.5) / maximum;
        var dy = (y - 0.5) / maximum;
        var factor = 1 + kr1 * (dx * dx + dy * dy);
        return (0.5 + maximum * factor * dx,
            0.5 + maximum * factor * dy);
    }

    private static byte[] ToBytes(ushort[] values)
    {
        var bytes = new byte[values.Length * sizeof(ushort)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}

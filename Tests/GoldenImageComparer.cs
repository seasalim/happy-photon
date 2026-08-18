using ImageMagick;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal readonly record struct GoldenComparison(double MeanDeltaE, double P99DeltaE);

internal enum GoldenComparisonDomain
{
    DisplaySrgb,
    LinearRec2020
}

internal static class GoldenImageComparer
{
    internal static readonly double[,] SrgbToXyzD65 =
        RgbColorSpaceMatrices.LinearSrgbToXyzD65PublishedRounded;

    public static GoldenComparison Compare(
        MagickImage expected,
        MagickImage actual,
        GoldenComparisonDomain domain)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            throw new InvalidOperationException(
                $"Image dimensions differ: expected {expected.Width}x{expected.Height}, " +
                $"actual {actual.Width}x{actual.Height}.");
        }

        var deltaValues = new double[checked((int)(expected.Width * expected.Height))];
        if (domain == GoldenComparisonDomain.DisplaySrgb)
        {
            using var expectedSrgb = Normalize(expected);
            using var actualSrgb = Normalize(actual);
            using var expectedPixels = expectedSrgb.GetPixels();
            using var actualPixels = actualSrgb.GetPixels();
            var expectedBytes = expectedPixels.ToByteArray(PixelMapping.RGB)
                ?? throw new InvalidOperationException("Could not read expected RGB pixels.");
            var actualBytes = actualPixels.ToByteArray(PixelMapping.RGB)
                ?? throw new InvalidOperationException("Could not read actual RGB pixels.");
            for (var pixelIndex = 0; pixelIndex < deltaValues.Length; pixelIndex++)
            {
                var channelIndex = pixelIndex * 3;
                deltaValues[pixelIndex] = DeltaE(
                    ToDisplayLab(expectedBytes, channelIndex),
                    ToDisplayLab(actualBytes, channelIndex));
            }
        }
        else
        {
            using var expectedPixels = expected.GetPixels();
            using var actualPixels = actual.GetPixels();
            var expectedValues = expectedPixels.ToShortArray(PixelMapping.RGB)
                ?? throw new InvalidOperationException("Could not read expected RGB pixels.");
            var actualValues = actualPixels.ToShortArray(PixelMapping.RGB)
                ?? throw new InvalidOperationException("Could not read actual RGB pixels.");
            for (var pixelIndex = 0; pixelIndex < deltaValues.Length; pixelIndex++)
            {
                var channelIndex = pixelIndex * 3;
                deltaValues[pixelIndex] = DeltaE(
                    ToLinearLab(expectedValues, channelIndex),
                    ToLinearLab(actualValues, channelIndex));
            }
        }

        Array.Sort(deltaValues);
        var mean = deltaValues.Average();
        var p99Index = Math.Max(0, (int)Math.Ceiling(deltaValues.Length * 0.99) - 1);
        return new GoldenComparison(mean, deltaValues[p99Index]);
    }

    private static MagickImage Normalize(MagickImage source)
    {
        var image = (MagickImage)source.Clone();
        CurrentPipelineGoldenRenderer.NormalizeToSrgb(image);
        return image;
    }

    private static LabColor ToDisplayLab(byte[] pixels, int index) =>
        ToLab(
            ToLinear(pixels[index] / 255.0),
            ToLinear(pixels[index + 1] / 255.0),
            ToLinear(pixels[index + 2] / 255.0),
            SrgbToXyzD65);

    private static LabColor ToLinearLab(ushort[] pixels, int index) =>
        ToLab(
            pixels[index] / (double)ushort.MaxValue,
            pixels[index + 1] / (double)ushort.MaxValue,
            pixels[index + 2] / (double)ushort.MaxValue,
            RgbColorSpaceMatrices.LinearRec2020ToXyzD65DerivedExact);

    private static LabColor ToLab(
        double r,
        double g,
        double b,
        double[,] rgbToXyz)
    {
        var x = (rgbToXyz[0, 0] * r + rgbToXyz[0, 1] * g +
            rgbToXyz[0, 2] * b) / 0.95047;
        var y = rgbToXyz[1, 0] * r + rgbToXyz[1, 1] * g +
            rgbToXyz[1, 2] * b;
        var z = (rgbToXyz[2, 0] * r + rgbToXyz[2, 1] * g +
            rgbToXyz[2, 2] * b) / 1.08883;
        var fx = LabTransform(x);
        var fy = LabTransform(y);
        var fz = LabTransform(z);
        return new LabColor(
            116 * fy - 16,
            500 * (fx - fy),
            200 * (fy - fz));
    }

    private static double ToLinear(double value) =>
        value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double LabTransform(double value)
    {
        const double epsilon = 216.0 / 24389.0;
        const double kappa = 24389.0 / 27.0;
        return value > epsilon
            ? Math.Cbrt(value)
            : (kappa * value + 16) / 116;
    }

    private static double DeltaE(LabColor first, LabColor second)
    {
        var deltaL = first.L - second.L;
        var deltaA = first.A - second.A;
        var deltaB = first.B - second.B;
        return Math.Sqrt(deltaL * deltaL + deltaA * deltaA + deltaB * deltaB);
    }

    private readonly record struct LabColor(double L, double A, double B);
}

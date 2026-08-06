using ImageMagick;

namespace HappyPhoton.Tests;

internal readonly record struct GoldenComparison(double MeanDeltaE, double P99DeltaE);

internal static class GoldenImageComparer
{
    public static GoldenComparison Compare(MagickImage expected, MagickImage actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            throw new InvalidOperationException(
                $"Image dimensions differ: expected {expected.Width}x{expected.Height}, " +
                $"actual {actual.Width}x{actual.Height}.");
        }

        using var expectedSrgb = Normalize(expected);
        using var actualSrgb = Normalize(actual);
        using var expectedPixels = expectedSrgb.GetPixels();
        using var actualPixels = actualSrgb.GetPixels();
        var expectedBytes = expectedPixels.ToByteArray(PixelMapping.RGB)
            ?? throw new InvalidOperationException("Could not read expected RGB pixels.");
        var actualBytes = actualPixels.ToByteArray(PixelMapping.RGB)
            ?? throw new InvalidOperationException("Could not read actual RGB pixels.");
        var deltaValues = new double[checked((int)(expected.Width * expected.Height))];
        for (var pixelIndex = 0; pixelIndex < deltaValues.Length; pixelIndex++)
        {
            var channelIndex = pixelIndex * 3;
            deltaValues[pixelIndex] = DeltaE(
                ToLab(expectedBytes, channelIndex),
                ToLab(actualBytes, channelIndex));
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

    private static LabColor ToLab(byte[] pixels, int index)
    {
        var r = ToLinear(pixels[index] / 255.0);
        var g = ToLinear(pixels[index + 1] / 255.0);
        var b = ToLinear(pixels[index + 2] / 255.0);
        var x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
        var y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        var z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;
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

using HappyPhoton.Services;

namespace HappyPhoton.Tests;

public readonly record struct PrecisionLab(double L, double A, double B);

internal static class PrecisionDeltaE
{
    internal static readonly double[,] SrgbToXyzD65 =
        RgbColorSpaceMatrices.LinearSrgbToXyzD65PublishedRounded;

    public static double FromSrgb(
        double red1,
        double green1,
        double blue1,
        double red2,
        double green2,
        double blue2) =>
        Ciede2000(
            ToLab(red1, green1, blue1),
            ToLab(red2, green2, blue2));

    public static PrecisionLab ToLab(double red, double green, double blue) =>
        SrgbLabConverter.ToLab(red, green, blue, SrgbToXyzD65);

    public static double Ciede2000(PrecisionLab first, PrecisionLab second)
    {
        var c1 = Math.Sqrt(first.A * first.A + first.B * first.B);
        var c2 = Math.Sqrt(second.A * second.A + second.B * second.B);
        var meanC = (c1 + c2) / 2;
        var meanC7 = Math.Pow(meanC, 7);
        var g = 0.5 * (1 - Math.Sqrt(meanC7 / (meanC7 + Math.Pow(25, 7))));
        var a1Prime = (1 + g) * first.A;
        var a2Prime = (1 + g) * second.A;
        var c1Prime = Math.Sqrt(a1Prime * a1Prime + first.B * first.B);
        var c2Prime = Math.Sqrt(a2Prime * a2Prime + second.B * second.B);
        var h1Prime = HueDegrees(first.B, a1Prime);
        var h2Prime = HueDegrees(second.B, a2Prime);

        var deltaLPrime = second.L - first.L;
        var deltaCPrime = c2Prime - c1Prime;
        var deltaHue = DeltaHue(h1Prime, h2Prime, c1Prime, c2Prime);
        var deltaHPrime = 2 * Math.Sqrt(c1Prime * c2Prime) *
            Math.Sin(DegreesToRadians(deltaHue / 2));

        var meanLPrime = (first.L + second.L) / 2;
        var meanCPrime = (c1Prime + c2Prime) / 2;
        var meanHPrime = MeanHue(h1Prime, h2Prime, c1Prime, c2Prime);
        var t = 1 - 0.17 * Math.Cos(DegreesToRadians(meanHPrime - 30)) +
            0.24 * Math.Cos(DegreesToRadians(2 * meanHPrime)) +
            0.32 * Math.Cos(DegreesToRadians(3 * meanHPrime + 6)) -
            0.20 * Math.Cos(DegreesToRadians(4 * meanHPrime - 63));
        var deltaTheta = 30 * Math.Exp(-Math.Pow((meanHPrime - 275) / 25, 2));
        var meanCPrime7 = Math.Pow(meanCPrime, 7);
        var rc = 2 * Math.Sqrt(
            meanCPrime7 / (meanCPrime7 + Math.Pow(25, 7)));
        var sl = 1 + 0.015 * Math.Pow(meanLPrime - 50, 2) /
            Math.Sqrt(20 + Math.Pow(meanLPrime - 50, 2));
        var sc = 1 + 0.045 * meanCPrime;
        var sh = 1 + 0.015 * meanCPrime * t;
        var rt = -Math.Sin(DegreesToRadians(2 * deltaTheta)) * rc;

        var l = deltaLPrime / sl;
        var c = deltaCPrime / sc;
        var h = deltaHPrime / sh;
        return Math.Sqrt(l * l + c * c + h * h + rt * c * h);
    }

    private static double HueDegrees(double b, double aPrime)
    {
        if (aPrime == 0 && b == 0)
        {
            return 0;
        }

        var degrees = RadiansToDegrees(Math.Atan2(b, aPrime));
        return degrees < 0 ? degrees + 360 : degrees;
    }

    private static double DeltaHue(
        double h1,
        double h2,
        double c1,
        double c2)
    {
        if (c1 * c2 == 0)
        {
            return 0;
        }

        var difference = h2 - h1;
        if (Math.Abs(difference) <= 180)
        {
            return difference;
        }

        return difference > 180 ? difference - 360 : difference + 360;
    }

    private static double MeanHue(
        double h1,
        double h2,
        double c1,
        double c2)
    {
        if (c1 * c2 == 0)
        {
            return h1 + h2;
        }

        if (Math.Abs(h1 - h2) <= 180)
        {
            return (h1 + h2) / 2;
        }

        return h1 + h2 < 360
            ? (h1 + h2 + 360) / 2
            : (h1 + h2 - 360) / 2;
    }

    private static double DegreesToRadians(double degrees) =>
        degrees * Math.PI / 180;

    private static double RadiansToDegrees(double radians) =>
        radians * 180 / Math.PI;
}

using Xunit;

namespace HappyPhoton.Tests;

public readonly record struct PrecisionLab(double L, double A, double B);

internal static class PrecisionDeltaE
{
    private const double D65X = 0.95047;
    private const double D65Y = 1.00000;
    private const double D65Z = 1.08883;

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

    public static PrecisionLab ToLab(double red, double green, double blue)
    {
        var r = Decode(red);
        var g = Decode(green);
        var b = Decode(blue);
        var x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / D65X;
        var y = (0.2126729 * r + 0.7151522 * g + 0.0721750 * b) / D65Y;
        var z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / D65Z;
        var fx = PivotXyz(x);
        var fy = PivotXyz(y);
        var fz = PivotXyz(z);
        return new PrecisionLab(
            116 * fy - 16,
            500 * (fx - fy),
            200 * (fy - fz));
    }

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

    private static double Decode(double value) =>
        value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double PivotXyz(double value) =>
        value > 216.0 / 24389
            ? Math.Cbrt(value)
            : 841.0 / 108 * value + 4.0 / 29;

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

public sealed class PrecisionDeltaETests
{
    // Published supplementary test data from Sharma, Wu, and Dalal (2005).
    public static TheoryData<PrecisionLab, PrecisionLab, double> SharmaVectors => new()
    {
        { new(50, 2.6772, -79.7751), new(50, 0, -82.7485), 2.0425 },
        { new(50, 3.1571, -77.2803), new(50, 0, -82.7485), 2.8615 },
        { new(50, 2.8361, -74.0200), new(50, 0, -82.7485), 3.4412 },
        { new(50, -1.3802, -84.2814), new(50, 0, -82.7485), 1.0000 },
        { new(50, -1.1848, -84.8006), new(50, 0, -82.7485), 1.0000 },
        { new(50, -0.9009, -85.5211), new(50, 0, -82.7485), 1.0000 },
        { new(50, 0, 0), new(50, -1, 2), 2.3669 },
        { new(50, -1, 2), new(50, 0, 0), 2.3669 },
        { new(50, 2.49, -0.001), new(50, -2.49, 0.0009), 7.1792 },
        { new(50, 2.49, -0.001), new(50, -2.49, 0.0010), 7.1792 },
        { new(50, 2.49, -0.001), new(50, -2.49, 0.0011), 7.2195 },
        { new(50, -0.001, 2.49), new(50, 0.0009, -2.49), 4.8045 },
        { new(50, -0.001, 2.49), new(50, 0.0010, -2.49), 4.8045 },
        { new(50, -0.001, 2.49), new(50, 0.0011, -2.49), 4.7461 },
        { new(50, 2.5, 0), new(50, 0, -2.5), 4.3065 },
        { new(50, 2.5, 0), new(73, 25, -18), 27.1492 },
        { new(50, 2.5, 0), new(61, -5, 29), 22.8977 },
        { new(50, 2.5, 0), new(56, -27, -3), 31.9030 },
        { new(50, 2.5, 0), new(58, 24, 15), 19.4535 },
        { new(50, 2.5, 0), new(50, 3.1736, 0.5854), 1.0000 },
        { new(50, 2.5, 0), new(50, 3.2972, 0), 1.0000 },
        { new(50, 2.5, 0), new(50, 1.8634, 0.5757), 1.0000 },
        { new(50, 2.5, 0), new(50, 3.2592, 0.3350), 1.0000 },
        { new(60.2574, -34.0099, 36.2677), new(60.4626, -34.1751, 39.4387), 1.2644 },
        { new(63.0109, -31.0961, -5.8663), new(62.8187, -29.7946, -4.0864), 1.2630 },
        { new(61.2901, 3.7196, -5.3901), new(61.4292, 2.2480, -4.9620), 1.8731 },
        { new(35.0831, -44.1164, 3.7933), new(35.0232, -40.0716, 1.5901), 1.8645 },
        { new(22.7233, 20.0904, -46.6940), new(23.0331, 14.9730, -42.5619), 2.0373 },
        { new(36.4612, 47.8580, 18.3852), new(36.2715, 50.5065, 21.2231), 1.4146 },
        { new(90.8027, -2.0831, 1.4410), new(91.1528, -1.6435, 0.0447), 1.4441 },
        { new(90.9257, -0.5406, -0.9208), new(88.6381, -0.8985, -0.7239), 1.5381 },
        { new(6.7747, -0.2908, -2.4247), new(5.8714, -0.0985, -2.2286), 0.6377 },
        { new(2.0776, 0.0795, -1.1350), new(0.9033, -0.0636, -0.5514), 0.9082 }
    };

    [Theory]
    [MemberData(nameof(SharmaVectors))]
    public void Ciede2000_MatchesPublishedReferenceVectors(
        PrecisionLab first,
        PrecisionLab second,
        double expected)
    {
        var actual = PrecisionDeltaE.Ciede2000(first, second);

        Assert.InRange(actual, expected - 0.00005, expected + 0.00005);
    }
}

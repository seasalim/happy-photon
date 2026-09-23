namespace HappyPhoton.Tests;

// Test-only double definition; no production OKLab/window calls or retained classification image.
internal static class LocalsRangeOracle
{
    internal readonly record struct Lab(double L, double A, double B)
    {
        internal double C => Math.Sqrt(A * A + B * B);
        internal double Hue => Wrap(Math.Atan2(B, A) * 180 / Math.PI);
    }

    internal readonly record struct LightWindow
    {
        internal double Lower { get; }
        internal double Upper { get; }
        internal double Softness { get; }
        internal static LightWindow Default => new(0, 1, .1);
        internal LightWindow(double lower, double upper, double softness)
        {
            Bound(lower, 0, 1); Bound(upper, lower, 1); Bound(softness, 0, .5);
            Lower = lower; Upper = upper; Softness = softness;
        }
        internal double Weight(double l)
        {
            if (Lower == Upper && Softness == 0 && Lower > 0 && Upper < 1) return 0;
            var left = Lower == 0 || l >= Lower ? 1 : Softness == 0 ? 0 : Smooth(1 + (l - Lower) / Softness);
            var right = Upper == 1 || l <= Upper ? 1 : Softness == 0 ? 0 : Smooth(1 + (Upper - l) / Softness);
            return left * right;
        }
    }

    internal readonly record struct HueWindow
    {
        internal double Center { get; }
        internal double Width { get; }
        internal double Softness { get; }
        internal static HueWindow Default => new(240, 60, 30);
        internal HueWindow(double center, double width, double softness)
        {
            Bound(center, 0, Math.BitDecrement(360)); Bound(width, 0, 360); Bound(softness, 0, 90);
            Center = center; Width = width; Softness = softness;
        }
        internal double Weight(double hue)
        {
            if (Width == 360) return 1;
            if (Width == 0 && Softness == 0) return 0;
            var d = Distance(hue, Center);
            return d <= Width / 2 ? 1 : Softness == 0 ? 0 : Smooth(1 - (d - Width / 2) / Softness);
        }
    }

    // BT.2020 primaries -> XYZ D65 -> Ottosson LMS, independently composed from
    // the committed colour-science oracle, as in OklabColorDerivationTests.
    private static readonly double[,] ToLms = CreateMatrix();
    internal static double[,] CreateMatrix()
    {
        var space = ColorScienceOracleData.Load().Space("linear-rec2020-d65");
        var xyz = ColorScienceMatrixAssertions.DeriveRgbToXyz(space.Primaries, space.WhitePoint);
        return PrecisionColorCases.Multiply(new[,]
        {
            { .8189330101, .3618667424, -.1288597137 },
            { .0329845436, .9293118715, .0361456387 },
            { .0482003018, .2643662691, .6338517070 }
        }, xyz);
    }

    internal static double ClassifyLightness(double r, double g, double b)
    {
        var l = Math.Cbrt(ToLms[0, 0] * r + ToLms[0, 1] * g + ToLms[0, 2] * b);
        var m = Math.Cbrt(ToLms[1, 0] * r + ToLms[1, 1] * g + ToLms[1, 2] * b);
        var s = Math.Cbrt(ToLms[2, 0] * r + ToLms[2, 1] * g + ToLms[2, 2] * b);
        return .2104542553 * l + .7936177850 * m - .0040720468 * s;
    }

    internal static Lab Classify(double r, double g, double b)
    {
        var l = Math.Cbrt(ToLms[0, 0] * r + ToLms[0, 1] * g + ToLms[0, 2] * b);
        var m = Math.Cbrt(ToLms[1, 0] * r + ToLms[1, 1] * g + ToLms[1, 2] * b);
        var s = Math.Cbrt(ToLms[2, 0] * r + ToLms[2, 1] * g + ToLms[2, 2] * b);
        return new(.2104542553 * l + .7936177850 * m - .0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + .4505937099 * s,
            .0259040371 * l + .7827717662 * m - .8086757660 * s);
    }

    internal static double Reliability(double c, double start = .01, double end = .04) =>
        Smooth((c - start) / (end - start)); // Pinned absolute pre-tone C ramp: .01-.04.
    internal static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    internal static double Wrap(double degrees) => (degrees % 360 + 360) % 360;
    internal static double Distance(double a, double b) => Math.Abs(Math.IEEERemainder(a - b, 360));
    private static void Bound(double value, double min, double max)
    {
        if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentOutOfRangeException(nameof(value));
    }

    internal static (double? Hue, string? Rejection, int Count) Pick(int width, int height,
        double x, double y, Func<int, Lab>? matchingBase, double start = .01, double end = .04)
    {
        if (matchingBase == null) return (null, "no matching base", 0);
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= width || y >= height)
            return (null, "off-image", 0);
        var radius = .004 * Math.Max(width, height);
        double a = 0, b = 0, chroma = 0;
        var count = 0;
        for (var row = Math.Max(0, (int)Math.Floor(y - radius)); row <= Math.Min(height - 1, (int)(y + radius)); row++)
        for (var col = Math.Max(0, (int)Math.Floor(x - radius)); col <= Math.Min(width - 1, (int)(x + radius)); col++)
        {
            if (Math.Pow(col + .5 - x, 2) + Math.Pow(row + .5 - y, 2) > radius * radius &&
                (col != (int)x || row != (int)y)) continue;
            var lab = matchingBase(row * width + col);
            a += lab.A; b += lab.B; chroma += lab.C; count++;
        }
        var mean = new Lab(0, a / count, b / count);
        if (Reliability(mean.C, start, end) < .5) return (null, "too neutral", count);
        if (mean.C / (chroma / count) < .75) return (null, "mixed colors", count);
        return (mean.Hue, null, count);
    }
}

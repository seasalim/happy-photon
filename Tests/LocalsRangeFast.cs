using static HappyPhoton.Tests.LocalsRangeOracle;

namespace HappyPhoton.Tests;

// Test-only candidate. The independent double oracle remains the definition.
internal sealed class LocalsRangeFast
{
    private const int Size = 262144;
    private const double Scale = Size / 4d;
    private static readonly double[] Roots = Enumerable.Range(0, Size + 1)
        .Select(i => Math.Cbrt(i / Scale)).ToArray(); // Shared 2 MiB, no per-frame field.
    private static readonly double[,] Matrix = CreateMatrix();
    internal const double LightError = 2e-8;
    internal const double ChromaError = 9e-8;
    private readonly (LightWindow L, HueWindow H)[] windows;

    internal LocalsRangeFast((LightWindow L, HueWindow H)[] windows) => this.windows = windows;

    // Linear interpolation error <= h² max|f''|/8, h=4/262144, f''=-2/(9*x^(5/3)).
    // Every used cell lies above .00998: root error <1.5e-8 including double rounding.
    // Multiplying by the absolute OKLab row sums gives |dL|<2e-8 and |d(a,b)|<9e-8.
    // Outside .01<=|LMS|<4 use the oracle (including extended/cancelling RGB).
    internal static Lab Approximate(double r, double g, double b, out bool exact)
    {
        var l = Matrix[0, 0] * r + Matrix[0, 1] * g + Matrix[0, 2] * b;
        var m = Matrix[1, 0] * r + Matrix[1, 1] * g + Matrix[1, 2] * b;
        var s = Matrix[2, 0] * r + Matrix[2, 1] * g + Matrix[2, 2] * b;
        exact = !InTable(l) || !InTable(m) || !InTable(s);
        if (exact) return Classify(r, g, b);
        l = Root(l); m = Root(m); s = Root(s);
        return new(.2104542553 * l + .7936177850 * m - .0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + .4505937099 * s,
            .0259040371 * l + .7827717662 * m - .8086757660 * s);
    }

    internal Lab ClassifyGuarded(double r, double g, double b, byte active, out bool fallback) =>
        ClassifyGuarded(r, g, b, active, out fallback, out _, out _);

    internal Lab ClassifyGuarded(double r, double g, double b, byte active, out bool fallback,
        out double c, out double hue)
    {
        var lab = Approximate(r, g, b, out fallback);
        c = lab.C; hue = lab.Hue;
        if (fallback || !NeedsOracle(lab, active, c, hue)) return lab;
        fallback = true;
        lab = Classify(r, g, b); // Once per pixel, shared by all selected windows.
        c = lab.C; hue = lab.Hue;
        return lab;
    }

    internal bool NeedsOracle(Lab lab, byte active, double c, double hue)
    {
        if (c <= 2 * ChromaError) return true;
        // asin(e/C) <= 2e/C for e/C<=.5; angle bound in degrees, including wrap.
        var hueError = 2 * ChromaError / c * (180 / Math.PI);
        for (var i = 0; i < windows.Length; i++)
        {
            if ((active & (1 << i)) == 0) continue;
            var (l, h) = windows[i];
            if (l.Lower > 0 && NearRamp(lab.L, l.Lower - l.Softness, l.Lower, LightError) ||
                l.Upper < 1 && NearRamp(lab.L, l.Upper, l.Upper + l.Softness, LightError)) return true;
            if (h.Width < 360 && NearRamp(Distance(hue, h.Center), h.Width / 2,
                    h.Width / 2 + h.Softness, hueError)) return true;
        }
        return false;
    }

    private static bool NearRamp(double value, double lo, double hi, double error)
    {
        // Guard both boundaries, and the whole shoulder when its slope is too steep.
        // Else smoothstep's derivative <=1.5 gives <=.000375 per window term;
        // reliability contributes <=50*9e-8. Product error is therefore <.001.
        if (hi - lo < 4000 * error) return value >= lo - error && value <= hi + error;
        return Math.Abs(value - lo) <= error || Math.Abs(value - hi) <= error;
    }

    private static bool InTable(double x) => Math.Abs(x) >= .01 && Math.Abs(x) < 4;
    private static double Root(double x)
    {
        var position = Math.Abs(x) * Scale;
        var index = (int)position;
        return Math.CopySign(Roots[index] + (Roots[index + 1] - Roots[index]) * (position - index), x);
    }
}

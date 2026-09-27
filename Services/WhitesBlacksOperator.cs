namespace HappyPhoton.Services;

internal sealed class WhitesBlacksOperator
{
    private const int MantissaBits = 11;
    private const int Shift = 52 - MantissaBits;
    private const long FirstBits = (1023L - 10) << 52;
    private const int LastIndex = 17 << MantissaBits;
    private readonly double[] _logGain = new double[LastIndex + 1];

    internal WhitesBlacksOperator(int whites, int blacks, bool raw)
    {
        for (var i = 0; i < _logGain.Length; i++)
        {
            var ev = Math.Log2(BitConverter.Int64BitsToDouble(FirstBits + ((long)i << Shift)));
            var span = ev > 0 ? raw ? 6.5 : Math.Log2(1 / .18) : raw ? 10 : 5.5;
            var t = Math.Clamp(Math.Abs(ev) / span, 0, 1);
            var ramp = raw ? t * t * (3 - 2 * t) : StandardRamp(t);
            _logGain[i] = (ev > 0 ? whites : blacks) / 100d * (raw ? 4 : 2.4) * ramp;
        }
    }

    private static double StandardRamp(double t)
    {
        const double edge = .02;
        return t < edge ? t * t / (2 * edge * (1 - edge)) : t > 1 - edge
            ? 1 - (1 - t) * (1 - t) / (2 * edge * (1 - edge))
            : (t - edge / 2) / (1 - edge);
    }

    // IEEE exponent and mantissa give a piecewise-linear luminance coordinate.
    // Division puts grey at the exact 1.0 node; no logarithm is evaluated per pixel.
    internal static double Position(double luminance) => luminance <= 0 ? 0 :
        Math.Clamp((BitConverter.DoubleToInt64Bits(luminance / .18) - FirstBits) /
            (double)(1L << Shift), 0, LastIndex);

    internal double LogGain(double position)
    {
        var i = (int)position;
        return i >= LastIndex ? _logGain[^1] :
            _logGain[i] + (_logGain[i + 1] - _logGain[i]) * (position - i);
    }

    internal double Gain(double position) => double.Exp2(LogGain(position));
}

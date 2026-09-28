using System.Runtime.CompilerServices;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

// Used only when a pixel has nonzero Whites/Blacks log gain. Legacy LUTs and
// legacy adjusted-local evaluation remain untouched, including their rounding.
internal sealed class ExtendedToneLut
{
    internal const int Intervals = 48 * 2048;
    private const int Shift = 41;
    private const long FirstBits = (1023L - 24) << 52;
    private const double First = 1.0 / (1 << 24);
    private const double Last = 1 << 24;
    private readonly double[] _values = new double[Intervals + 1];
    private readonly bool[] _analytic = new bool[Intervals];
    private readonly Func<double, double> _evaluate;
    private static readonly ConditionalWeakTable<ToneLuts, Channels> Cache = new();
    internal sealed record Channels(ExtendedToneLut Red, ExtendedToneLut Green, ExtendedToneLut Blue);

    internal ExtendedToneLut(Func<double, double> evaluate, Func<double, int>? curveCell = null,
        RenderExecutionOptions? execution = null)
    {
        _evaluate = evaluate;
        Parallel.For(0, Intervals + 1, execution?.ParallelOptions ?? new ParallelOptions(), i => _values[i] = evaluate(Node(i)));
        Parallel.For(0, Intervals, execution?.ParallelOptions ?? new ParallelOptions(), i =>
        {
            var lo = Node(i); var hi = Node(i + 1);
            if (curveCell != null && curveCell(lo) != curveCell(hi))
            {
                _analytic[i] = true;
                return;
            }
            // Curve knots and clamp knees need the analytic evaluator when a cell
            // straddles them. Also guard smooth interpolation to well below one
            // Q16 code after RAW's outset and sRGB's maximum slope (12.92).
            for (var part = 1; part < 4; part++)
            {
                var fraction = part * .25;
                if (Math.Abs(evaluate(lo + (hi - lo) * fraction) -
                    (_values[i] + (_values[i + 1] - _values[i]) * fraction)) > 1e-8)
                {
                    _analytic[i] = true;
                    break;
                }
            }
        });
    }

    internal static double Node(int index) => BitConverter.Int64BitsToDouble(FirstBits + ((long)index << Shift));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal double Evaluate(double input)
    {
        // Unbounded local stacking and unusual source exposure remain headroom-safe.
        // No clamped input or Q16 intermediate, even outside the sampled domain.
        if (!(input >= First && input < Last)) return _evaluate(input);
        var position = (BitConverter.DoubleToInt64Bits(input) - FirstBits) / (double)(1L << Shift);
        var i = (int)position;
        return i >= Intervals || _analytic[i] ? _evaluate(input) :
            _values[i] + (_values[i + 1] - _values[i]) * (position - i);
    }

    internal static Channels ForRaw(ToneLuts key, AgxToneParameters parameters, double fold,
        RenderExecutionOptions? execution = null)
    {
        if (Cache.TryGetValue(key, out var hit)) return hit;
        var composed = ComposeRaw(parameters, fold, execution);
        return Cache.GetValue(key, _ => composed);
    }

    internal static Channels ForStandard(ToneLuts key, ToneParams parameters,
        RenderExecutionOptions? execution = null)
    {
        if (Cache.TryGetValue(key, out var hit)) return hit;
        var composed = ComposeStandard(parameters, execution);
        return Cache.GetValue(key, _ => composed);
    }

    internal static Channels ComposeRaw(AgxToneParameters parameters, double fold,
        RenderExecutionOptions? execution = null)
    {
        var p = parameters with { Curve = parameters.Curve.Clone(), CurveRed = parameters.CurveRed?.Clone(),
            CurveGreen = parameters.CurveGreen?.Clone(), CurveBlue = parameters.CurveBlue?.Clone() };
        var exposure = Math.Pow(2, p.ExposureEv + p.SourceExposureEv); var logFold = Math.Log2(fold);
        var slope = AgxToneEngine.Slope(p.Contrast); var toe = AgxToneEngine.ToePower(p.Shadows);
        var shoulder = AgxToneEngine.ShoulderPower(p.Highlights);
        var toeScale = AgxToneEngine.TailScale(AgxToneEngine.XPivot, AgxToneEngine.YPivot, slope, toe);
        var shoulderScale = AgxToneEngine.TailScale(1 - AgxToneEngine.XPivot, 1 - AgxToneEngine.YPivot, slope, shoulder);
        return Compose(p.CurveRed, p.CurveGreen, p.CurveBlue, channel =>
        {
            var identity = p.Curve.IsIdentity() && (channel?.IsIdentity() ?? true);
            return new(value => AgxToneEngine.EvaluateToneExtendedUnchecked(value, p, exposure, logFold,
                slope, toe, shoulder, channel, toeScale, shoulderScale, identity), identity ? null : value =>
                CurveCell(ToneLut.SrgbEncode(AgxToneEngine.EvaluateToneExtendedUnchecked(value, p, exposure,
                    logFold, slope, toe, shoulder, null, toeScale, shoulderScale, true)), p.Curve, channel), execution);
        });
    }

    internal static Channels ComposeStandard(ToneParams parameters, RenderExecutionOptions? execution = null)
    {
        var p = parameters with { Curve = parameters.Curve.Clone(), CurveRed = parameters.CurveRed?.Clone(),
            CurveGreen = parameters.CurveGreen?.Clone(), CurveBlue = parameters.CurveBlue?.Clone() };
        var uncurved = p with { Curve = new(), CurveRed = null, CurveGreen = null, CurveBlue = null };
        return Compose(p.CurveRed, p.CurveGreen, p.CurveBlue, channel => new(value => ToneLut.Evaluate(p, value, channel),
            p.Curve.IsIdentity() && (channel?.IsIdentity() ?? true) ? null : value =>
                CurveCell(ToneLut.Evaluate(uncurved, value), p.Curve, channel), execution));
    }

    // A cell may interpolate only within one segment of each 256-node curve.
    // This catches even narrow peaks that interior error probes could miss.
    private static int CurveCell(double input, CurveData master, CurveData? channel)
    {
        var channelActive = channel?.IsIdentity() == false;
        var first = channelActive ? Math.Min(254, (int)(input * 255)) : 0;
        var output = channelActive ? ToneLut.EvaluateCurve(channel!, input) : input;
        var second = master.IsIdentity() ? 0 : Math.Min(254, (int)(output * 255));
        return (first << 8) | second;
    }

    private static Channels Compose(CurveData? red, CurveData? green, CurveData? blue,
        Func<CurveData?, ExtendedToneLut> compose)
    {
        var r = red?.IsIdentity() == false; var g = green?.IsIdentity() == false; var b = blue?.IsIdentity() == false;
        var shared = r && g && b ? null : compose(null);
        return new(r ? compose(red) : shared!, g ? compose(green) : shared!, b ? compose(blue) : shared!);
    }
}

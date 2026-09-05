using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    private enum Candidate { Analytic, Linear16, Log }
    private readonly record struct PreparedLocal(DcpRenderMatrix Matrix, double Gain, double Weight,
        double Saturation, bool ColorActive);

    private static PreparedLocal[] Prepare(Local[] locals) => locals.Where(l => l.Active)
        .Select(l => new PreparedLocal(new(l.Matrix), Math.Pow(2, l.Ev), l.Weight,
            1 + l.Saturation / 100, l.Mired != 0 || l.Tint != 0 || l.Saturation != 0)).ToArray();

    private static Rgb Transform(DcpRenderMatrix m, Rgb v) => new(
        m.Row0(v.R, v.G, v.B), m.Row1(v.R, v.G, v.B), m.Row2(v.R, v.G, v.B));

    private static Rgb CandidateLocals(Rgb v, PreparedLocal[] locals, bool mono)
    {
        foreach (var local in locals)
        {
            var adjusted = v;
            if (!mono && local.ColorActive)
            {
                adjusted = Transform(local.Matrix, v);
                var y = 0.2627002120112671 * adjusted.R + 0.6779980715188708 * adjusted.G
                    + 0.0593017164698620 * adjusted.B;
                adjusted = new(y + local.Saturation * (adjusted.R - y),
                    y + local.Saturation * (adjusted.G - y), y + local.Saturation * (adjusted.B - y));
            }
            adjusted *= local.Gain;
            // Preserve signed WB/chroma results until the post-inset tone-input clamp.
            v = new(v.R + local.Weight * (adjusted.R - v.R),
                v.G + local.Weight * (adjusted.G - v.G),
                v.B + local.Weight * (adjusted.B - v.B));
        }
        return v;
    }

    // Scalar registers only. Table/parameter construction belongs to render setup, not the pixel loop.
    private sealed class Kernel
    {
        private readonly Candidate _candidate;
        private readonly AgxToneParameters _raw;
        private readonly ToneParams _standard;
        private readonly bool _isRaw;
        private readonly DcpRenderMatrix _wb;
        private readonly DcpRenderMatrix _inset = new(AgxToneEngine.InsetMatrix);
        private readonly DcpRenderMatrix _outset = new(AgxToneEngine.OutsetMatrix);
        private readonly AgxCrossing _legacy;
        private readonly ToneLuts _legacyStandard;
        private readonly DcpRenderMatrix _normalizedWb;
        private readonly double[][] _tables;
        private readonly double _gain;
        private readonly double _slope;
        private readonly double _toe;
        private readonly double _shoulder;
        private readonly CurveData?[] _channels;

        internal Kernel(Candidate candidate, AgxToneParameters raw, ToneParams standard,
            double[,] wb, bool isRaw = true)
        {
            _candidate = candidate;
            _raw = raw;
            _standard = standard;
            _isRaw = isRaw;
            _wb = new(wb);
            _legacy = new(raw, wb);
            var normalized = ChromaticAdaptation.NormalizeForRender(wb);
            _normalizedWb = new(normalized.Matrix);
            _legacyStandard = ToneLut.ComposeCached(standard with { Fold = normalized.Fold });
            _gain = Math.Pow(2, isRaw ? raw.ExposureEv + raw.SourceExposureEv : standard.ExposureEv) *
                (isRaw ? 1 : standard.Fold);
            _slope = AgxToneEngine.Slope(raw.Contrast);
            _toe = AgxToneEngine.ToePower(raw.Shadows);
            _shoulder = AgxToneEngine.ShoulderPower(raw.Highlights);
            _channels = isRaw ? [raw.CurveRed, raw.CurveGreen, raw.CurveBlue]
                : [standard.CurveRed, standard.CurveGreen, standard.CurveBlue];
            _tables = new double[3][];
            if (candidate == Candidate.Analytic) return;
            for (var channel = 0; channel < 3; channel++)
            {
                if (channel > 0 && ReferenceEquals(_channels[channel], _channels[0]))
                {
                    _tables[channel] = _tables[0];
                    continue;
                }
                var table = new double[65536];
                for (var i = 0; i < table.Length; i++)
                {
                    var position = i / 65535.0;
                    var exposed = candidate == Candidate.Linear16 ? 16 * position * _gain
                        : 0.18 * Math.Pow(2, position * 16.5 - 10);
                    table[i] = AnalyticExposed(exposed, channel);
                }
                _tables[channel] = table;
            }
        }

        internal Rgb Render(Rgb input, PreparedLocal[] locals, bool mono = false)
        {
            // Retain the existing normalized WB/LUT arithmetic for the exact bypass.
            // Extended synthetic inputs cannot take that bounded production path.
            if (locals.Length == 0 && input.R <= 1 && input.G <= 1 && input.B <= 1 && !mono)
                return Legacy(input);
            var v = CandidateLocals(mono ? input : Transform(_wb, input), locals, mono);
            if (_isRaw) v = Transform(_inset, v);
            v = new(Tone(v.R, 0), Tone(v.G, 1), Tone(v.B, 2));
            if (!_isRaw) return v;
            v = Transform(_outset, v);
            return new(Encode(Clamp(v.R)), Encode(Clamp(v.G)), Encode(Clamp(v.B)));
        }

        internal Rgb Legacy(Rgb v)
        {
            if (_isRaw)
            {
                var result = _legacy.TransformInterpolated(new(v.R, v.G, v.B));
                return new(result.Red, result.Green, result.Blue);
            }
            v = Transform(_normalizedWb, v);
            return new(Interpolate(_legacyStandard.Red, Clamp(v.R)),
                Interpolate(_legacyStandard.Green, Clamp(v.G)), Interpolate(_legacyStandard.Blue, Clamp(v.B)));
        }

        private double Tone(double value, int channel)
        {
            var exposed = Math.Max(0, value) * _gain;
            if (_candidate == Candidate.Analytic) return AnalyticExposed(exposed, channel);
            // B composes exposure/fold into nodes over the scene-linear [0,16] allocation.
            // C indexes after exposure/fold. Overflow goes to the tone window edge, never Q16.
            if (_candidate == Candidate.Linear16)
                return value > 16 ? AnalyticExposed(0.18 * Math.Pow(2, 6.5), channel)
                    : Interpolate(_tables[channel], Math.Max(0, value) / 16);
            var position = exposed <= 0 ? 0 : Clamp((Math.Log2(exposed / 0.18) + 10) / 16.5);
            return Interpolate(_tables[channel], position);
        }

        private double AnalyticExposed(double exposed, int channel)
        {
            if (!_isRaw)
                return ToneLut.Evaluate(_standard with { ExposureEv = 0, Fold = 1 }, exposed, _channels[channel]);
            var x = exposed <= 0 ? 0 : Clamp((Math.Log2(exposed) - Math.Log2(0.18) + 10) / 16.5);
            var u = AgxToneEngine.EvaluateSigmoid(x, _slope, _toe, _shoulder);
            return ToneLut.SrgbDecode(Clamp(ToneLut.EvaluateComposedCurve(_raw.Curve, _channels[channel],
                ToneLut.SrgbEncode(Math.Pow(u, 2.2)))));
        }
    }

    private static double Interpolate(double[] table, double position)
    {
        position = Clamp(position) * (table.Length - 1);
        var i = (int)position;
        var fraction = position - i;
        return table[i] * (1 - fraction) + table[Math.Min(i + 1, table.Length - 1)] * fraction;
    }

    private static AgxToneParameters Raw(double ev = 0) => new(ev, 0, 0, 0, 0, new());
    private static ToneParams Standard(double ev = 0) => new(ev, 1, 0, 0, 0, 0, false, new());
}

using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

public sealed partial class LocalsContractPrototypeTests
{
    private readonly record struct Rgb(double R, double G, double B)
    {
        public static Rgb operator *(Rgb v, double s) => new(v.R * s, v.G * s, v.B * s);
        public static Rgb operator +(Rgb a, Rgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
        public static Rgb operator -(Rgb a, Rgb b) => new(a.R - b.R, a.G - b.G, a.B - b.B);
        internal Rgb Quantize() => new(Code(R) / 65535.0, Code(G) / 65535.0, Code(B) / 65535.0);
    }

    private readonly record struct Local(double Ev, double Weight, double Mired, double Tint,
        double Saturation, double[,] Matrix)
    {
        internal bool Active => Weight != 0 && (Ev != 0 || Mired != 0 || Tint != 0 || Saturation != 0);
        internal static Local Create(double ev = 0, double weight = 1, double mired = 0,
            double tint = 0, double saturation = 0, double kelvin = 6504, double globalTint = 0) =>
            new(ev, weight, mired, tint, saturation,
                WhiteBalanceModel.CreateMatrix(1e6 / (1e6 / kelvin + mired),
                    globalTint + tint, kelvin, globalTint));
    }

    private static double Clamp(double v) => Math.Clamp(v, 0, 1);
    private static int Code(double v) => (int)Math.Round(Clamp(v) * 65535, MidpointRounding.AwayFromZero);
    private static double Encode(double v) => v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
    private static double Decode(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    private static Rgb Matrix(double[,] m, Rgb v) => new(
        m[0, 0] * v.R + m[0, 1] * v.G + m[0, 2] * v.B,
        m[1, 0] * v.R + m[1, 1] * v.G + m[1, 2] * v.B,
        m[2, 0] * v.R + m[2, 1] * v.G + m[2, 2] * v.B);

    // Independent double oracle from TONE_ENGINE.md §4 and RENDER.md §5.
    // No production tone evaluators, input clamp, Q16 intermediate, or frame allocation.
    private static double ReferenceRaw(double value, AgxToneParameters p, double fold = 1,
        CurveData? channel = null)
    {
        var exposed = value * Math.Pow(2, p.ExposureEv + p.SourceExposureEv) * fold;
        var x = exposed <= 0 ? 0 : Clamp((Math.Log2(exposed / 0.18) + 10) / 16.5);
        const double xp = 10 / 16.5;
        var yp = Math.Pow(0.18, 1 / 2.2);
        var slope = 2 * Math.Pow(2, p.Contrast / 200.0);
        var lower = x < xp;
        var power = lower ? 3 * Math.Pow(2, -p.Shadows / 100.0)
            : 3.25 * Math.Pow(2, p.Highlights / 100.0);
        var dx = lower ? xp : 1 - xp;
        var dy = lower ? yp : 1 - yp;
        var scale = slope * dx / Math.Pow(Math.Pow(slope * dx / dy, power) - 1, 1 / power);
        var distance = slope * Math.Abs(x - xp) / scale;
        var tail = scale * distance / Math.Pow(1 + Math.Pow(distance, power), 1 / power);
        var sigmoid = Clamp(yp + (lower ? -tail : tail));
        return Decode(Clamp(ReferenceCurve(p.Curve,
            ReferenceCurve(channel, Encode(Math.Pow(sigmoid, 2.2))))));
    }

    private static double ReferenceCurve(CurveData? curve, double v)
    {
        if (curve == null || curve.IsIdentity()) return v;
        var position = Clamp(v) * (curve.LookupTable.Length - 1);
        var i = (int)position;
        var f = position - i;
        return (curve.LookupTable[i] + f *
            (curve.LookupTable[Math.Min(i + 1, curve.LookupTable.Length - 1)] - curve.LookupTable[i])) / 255;
    }

    private static double ReferenceStandard(double value, ToneParams p, CurveData? channel = null)
    {
        var exposed = Math.Max(0, value) * Math.Pow(2, p.ExposureEv) * p.Fold;
        var knee = 1 + Math.Min(0, p.Highlights) * 0.0055;
        var shoulder = knee == 1 ? Math.Min(exposed, 1) : exposed <= knee ? exposed
            : knee + (1 - knee) * Math.Tanh((exposed - knee) / (1 - knee));
        var v = Encode(Math.Min(shoulder, 1));
        if (p.BaseLookEnabled)
            v += 0.012 * Math.Pow(1 - v, 3) - 0.10 * Math.Sin(2 * Math.PI * v) * 4 * v * (1 - v)
                - 0.03 * Math.Pow(v, 3);
        v = Clamp(v + p.Brightness * 0.0035);
        v = Clamp(0.5 + (v - 0.5) * Math.Tan(Math.PI / 4 * (1 + p.Contrast * 0.006)));
        v += p.Shadows * 0.0035 * v * Math.Pow(1 - v, 3);
        v = Clamp(v + Math.Max(p.Highlights, 0) * 0.003 * Math.Pow(v, 3));
        return Clamp(ReferenceCurve(p.Curve, ReferenceCurve(channel, v)));
    }

    private static Rgb ReferenceLocals(Rgb v, Local[] locals, bool mono)
    {
        foreach (var local in locals)
        {
            if (!local.Active) continue;
            var adjusted = mono ? v : Matrix(local.Matrix, v);
            if (!mono)
            {
                var y = 0.2627002120112671 * adjusted.R + 0.6779980715188708 * adjusted.G
                    + 0.0593017164698620 * adjusted.B;
                var chroma = adjusted - new Rgb(y, y, y);
                adjusted = new Rgb(y, y, y) + chroma * (1 + local.Saturation / 100);
            }
            adjusted *= Math.Pow(2, local.Ev);
            v = v + (adjusted - v) * local.Weight;
        }
        return v;
    }

    private static Rgb Reference(Rgb v, AgxToneParameters raw, ToneParams standard,
        double[,] wb, Local[] locals, bool isRaw = true, bool mono = false)
    {
        v = ReferenceLocals(mono ? v : Matrix(wb, v), locals, mono);
        if (!isRaw) return new(ReferenceStandard(v.R, standard, standard.CurveRed),
            ReferenceStandard(v.G, standard, standard.CurveGreen), ReferenceStandard(v.B, standard, standard.CurveBlue));
        v = Matrix(AgxToneEngine.InsetMatrix, v);
        v = new(ReferenceRaw(v.R, raw, channel: raw.CurveRed), ReferenceRaw(v.G, raw, channel: raw.CurveGreen),
            ReferenceRaw(v.B, raw, channel: raw.CurveBlue));
        v = Matrix(AgxToneEngine.OutsetMatrix, v);
        return new(Encode(Clamp(v.R)), Encode(Clamp(v.G)), Encode(Clamp(v.B)));
    }
}

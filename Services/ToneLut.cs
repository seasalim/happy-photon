using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal readonly record struct ToneParams(
    double ExposureEv,
    double Fold,
    int Brightness,
    int Contrast,
    int Shadows,
    int Highlights,
    bool BaseLookEnabled,
    CurveData Curve);

internal static class ToneLut
{
    internal const int Length = 4096;

    public static ushort[] Compose(ToneParams parameters)
    {
        var lut = new ushort[Length];
        var gain = ExposureGain(
            parameters.ExposureEv,
            parameters.Fold);
        var knee = HighlightKnee(parameters.Highlights);
        var contrastSlope = ContrastSlope(parameters.Contrast);

        for (var i = 0; i < lut.Length; i++)
        {
            var exposed = i / (double)(Length - 1) * gain;
            var shouldered = HighlightShoulder(exposed, knee);
            var display = SrgbEncode(Math.Min(shouldered, 1));
            var looked = parameters.BaseLookEnabled ? BaseLook(display) : display;
            var brightened = ApplyBrightness(looked, parameters.Brightness);
            var contrasted = ApplyContrast(brightened, contrastSlope);
            var shadowed = ApplyShadows(contrasted, parameters.Shadows);
            var highlighted = ApplyPositiveHighlights(shadowed, parameters.Highlights);
            var curved = EvaluateCurve(parameters.Curve, highlighted);
            lut[i] = (ushort)Math.Round(
                Math.Clamp(curved, 0, 1) * ushort.MaxValue,
                MidpointRounding.AwayFromZero);
        }

        return lut;
    }

    internal static double ExposureGain(double exposureEv, double fold) =>
        Math.Pow(2, exposureEv) * fold;

    internal static double SrgbEncode(double value) =>
        value <= 0.0031308
            ? 12.92 * value
            : 1.055 * Math.Pow(value, 1 / 2.4) - 0.055;

    internal static double SrgbDecode(double value) =>
        value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);

    internal static double HighlightKnee(int highlights) =>
        1 + Math.Min(highlights, 0) / 100.0 * 0.55;

    internal static double HighlightShoulder(double value, double knee)
    {
        if (knee == 1)
        {
            return Math.Min(value, 1);
        }

        if (value <= knee)
        {
            return value;
        }

        return knee + (1 - knee) * Math.Tanh((value - knee) / (1 - knee));
    }

    internal static double BaseLook(double value) =>
        value +
        0.012 * Math.Pow(1 - value, 3) -
        0.10 * Math.Sin(2 * Math.PI * value) * 4 * value * (1 - value) -
        0.03 * Math.Pow(value, 3);

    internal static double ApplyBrightness(double value, int brightness) =>
        Math.Clamp(value + brightness / 100.0 * 0.35, 0, 1);

    internal static double ContrastSlope(int contrast) =>
        Math.Tan(Math.PI / 4 * (1 + contrast / 100.0 * 0.6));

    internal static double ApplyContrast(double value, double slope) =>
        Math.Clamp(0.5 + (value - 0.5) * slope, 0, 1);

    internal static double ApplyShadows(double value, int shadows) =>
        value + shadows / 100.0 * 0.35 * value * Math.Pow(1 - value, 3);

    internal static double ApplyPositiveHighlights(double value, int highlights) =>
        Math.Clamp(
            value + Math.Max(highlights, 0) / 100.0 * 0.30 * Math.Pow(value, 3),
            0,
            1);

    internal static double EvaluateCurve(CurveData curve, double value)
    {
        if (curve.IsIdentity())
        {
            return value;
        }

        var position = value * (curve.LookupTable.Length - 1);
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = Math.Min(lowerIndex + 1, curve.LookupTable.Length - 1);
        var fraction = position - lowerIndex;
        return (curve.LookupTable[lowerIndex] * (1 - fraction) +
                curve.LookupTable[upperIndex] * fraction) / byte.MaxValue;
    }
}

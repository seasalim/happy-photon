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
    CurveData Curve,
    CurveData? CurveRed = null,
    CurveData? CurveGreen = null,
    CurveData? CurveBlue = null);

internal sealed record ToneLuts(double[] Red, double[] Green, double[] Blue);

internal static class ToneLut
{
    internal const int Length = ushort.MaxValue + 1;

    public static ToneLuts Compose(ToneParams parameters) =>
        ChannelCurveLutComposer.Compose(
            parameters.CurveRed,
            parameters.CurveGreen,
            parameters.CurveBlue,
            channelCurve => Compose(parameters, channelCurve));

    private static double[] Compose(
        ToneParams parameters,
        CurveData? channelCurve)
    {
        var lut = new double[Length];
        var workers = Math.Min(
            Environment.ProcessorCount,
            Math.Max(1, Length / 8192));
        Parallel.For(0, workers, worker =>
        {
            var start = Length * worker / workers;
            var end = Length * (worker + 1) / workers;
            for (var index = start; index < end; index++)
            {
                lut[index] = Evaluate(
                    parameters,
                    index / (double)(Length - 1),
                    channelCurve);
            }
        });

        return lut;
    }

    internal static double Evaluate(ToneParams parameters, double input) =>
        Evaluate(parameters, input, channelCurve: null);

    internal static double Evaluate(
        ToneParams parameters,
        double input,
        CurveData? channelCurve)
    {
        var gain = ExposureGain(
            parameters.ExposureEv,
            parameters.Fold);
        var knee = HighlightKnee(parameters.Highlights);
        var contrastSlope = ContrastSlope(parameters.Contrast);
        var exposed = input * gain;
        var shouldered = HighlightShoulder(exposed, knee);
        var display = SrgbEncode(Math.Min(shouldered, 1));
        var looked = parameters.BaseLookEnabled ? BaseLook(display) : display;
        var brightened = ApplyBrightness(looked, parameters.Brightness);
        var contrasted = ApplyContrast(brightened, contrastSlope);
        var shadowed = ApplyShadows(contrasted, parameters.Shadows);
        var highlighted = ApplyPositiveHighlights(shadowed, parameters.Highlights);
        return Math.Clamp(
            EvaluateComposedCurve(
                parameters.Curve,
                channelCurve,
                highlighted),
            0,
            1);
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

    internal static double EvaluateComposedCurve(
        CurveData composite,
        CurveData? channel,
        double value)
    {
        var channelOutput = channel == null
            ? value
            : EvaluateCurve(channel, value);
        return EvaluateCurve(composite, channelOutput);
    }
}

internal static class ChannelCurveLutComposer
{
    internal static ToneLuts Compose(
        CurveData? red,
        CurveData? green,
        CurveData? blue,
        Func<CurveData?, double[]> compose)
    {
        var redActive = IsActive(red);
        var greenActive = IsActive(green);
        var blueActive = IsActive(blue);
        double[]? shared = redActive && greenActive && blueActive
            ? null
            : compose(null);

        return new ToneLuts(
            redActive ? compose(red) : shared!,
            greenActive ? compose(green) : shared!,
            blueActive ? compose(blue) : shared!);
    }

    private static bool IsActive(CurveData? curve) =>
        curve != null && !curve.IsIdentity();
}

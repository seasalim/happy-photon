using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal readonly record struct AgxToneParameters(
    double ExposureEv,
    double SourceExposureEv,
    int Contrast,
    int Highlights,
    int Shadows,
    CurveData Curve);

internal static class AgxToneEngine
{
    internal const double MiddleGrey = 0.18;
    internal const double EvBelowGrey = 10.0;
    internal const double EvAboveGrey = 6.5;
    internal const double EvWindow = EvBelowGrey + EvAboveGrey;
    internal const double XPivot = EvBelowGrey / EvWindow;
    internal const double NeutralSlope = 2.0;
    internal const double NeutralToePower = 3.0;
    internal const double NeutralShoulderPower = 3.25;
    internal const double DisplayGamma = 2.2;

    internal static readonly double YPivot =
        Math.Pow(MiddleGrey, 1.0 / DisplayGamma);

    internal static readonly double[,] InsetMatrix =
    {
        { 0.9722125648757899, 0.0005798182049564, 0.0272076169192538 },
        { 0.0236356386540170, 0.8511231029574200, 0.1252412583885629 },
        { 0.0809977588044689, 0.0815268062292870, 0.8374754349662439 }
    };

    internal static readonly double[,] OutsetMatrix =
    {
        { 1.0313429748257903, 0.0025432830319416, -0.0338862578577319 },
        { -0.0141655132770723, 1.1919580980795306, -0.1777925848024580 },
        { -0.0983689754038757, -0.1162810669486410, 1.2146500423525171 }
    };

    internal static double Slope(int contrast) =>
        NeutralSlope * Math.Pow(2, contrast / 200.0);

    internal static double ShoulderPower(int highlights) =>
        NeutralShoulderPower * Math.Pow(2, highlights / 100.0);

    internal static double ToePower(int shadows) =>
        NeutralToePower * Math.Pow(2, -shadows / 100.0);

    internal static double NormalizeLog(
        double value,
        double exposureEv,
        double sourceExposureEv,
        double fold)
    {
        var exposed = value * Math.Pow(2, exposureEv + sourceExposureEv);
        if (exposed <= 0)
        {
            return 0;
        }

        var normalized = (Math.Log2(exposed) + Math.Log2(fold) -
            Math.Log2(MiddleGrey) + EvBelowGrey) / EvWindow;
        return Math.Clamp(normalized, 0, 1);
    }

    internal static double EvaluateSigmoid(
        double x,
        double slope,
        double toePower,
        double shoulderPower)
    {
        x = Math.Clamp(x, 0, 1);
        if (x == XPivot)
        {
            return YPivot;
        }

        if (x < XPivot)
        {
            var scale = TailScale(XPivot, YPivot, slope, toePower);
            var distance = slope * (XPivot - x) / scale;
            return Math.Clamp(
                YPivot - scale * PowerHyperbolic(distance, toePower),
                0,
                1);
        }

        var shoulderScale = TailScale(
            1 - XPivot,
            1 - YPivot,
            slope,
            shoulderPower);
        var shoulderDistance = slope * (x - XPivot) / shoulderScale;
        return Math.Clamp(
            YPivot + shoulderScale *
                PowerHyperbolic(shoulderDistance, shoulderPower),
            0,
            1);
    }

    internal static double EvaluateTone(
        double value,
        AgxToneParameters parameters,
        double fold)
    {
        Validate(parameters, fold);
        return EvaluateToneUnchecked(
            value,
            parameters,
            Math.Pow(2, parameters.ExposureEv + parameters.SourceExposureEv),
            Math.Log2(fold),
            Slope(parameters.Contrast),
            ToePower(parameters.Shadows),
            ShoulderPower(parameters.Highlights));
    }

    internal static double EvaluateToneUnchecked(
        double value,
        AgxToneParameters parameters,
        double exposureGain,
        double log2Fold,
        double slope,
        double toePower,
        double shoulderPower)
    {
        var x = NormalizeLog(
            Math.Clamp(value, 0, 1),
            exposureGain,
            log2Fold);
        var encoded22 = EvaluateSigmoid(
            x,
            slope,
            toePower,
            shoulderPower);
        var displayLinear = Math.Pow(encoded22, DisplayGamma);
        var curveInput = ToneLut.SrgbEncode(displayLinear);
        var curveOutput = ToneLut.EvaluateCurve(parameters.Curve, curveInput);
        return ToneLut.SrgbDecode(Math.Clamp(curveOutput, 0, 1));
    }

    private static double NormalizeLog(
        double value,
        double exposureGain,
        double log2Fold)
    {
        var exposed = value * exposureGain;
        if (exposed <= 0)
        {
            return 0;
        }

        var normalized = (Math.Log2(exposed) + log2Fold -
            Math.Log2(MiddleGrey) + EvBelowGrey) / EvWindow;
        return Math.Clamp(normalized, 0, 1);
    }

    internal static void Validate(AgxToneParameters parameters, double fold)
    {
        ArgumentNullException.ThrowIfNull(parameters.Curve);
        ArgumentOutOfRangeException.ThrowIfLessThan(parameters.Contrast, -100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parameters.Contrast, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(parameters.Highlights, -100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parameters.Highlights, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(parameters.Shadows, -100);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parameters.Shadows, 100);
        if (!double.IsFinite(parameters.ExposureEv) ||
            !double.IsFinite(parameters.SourceExposureEv))
        {
            throw new ArgumentOutOfRangeException(
                nameof(parameters),
                "Exposure values must be finite.");
        }
        if (!double.IsFinite(fold) || fold < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fold),
                "Fold must be finite and at least one.");
        }
    }

    private static double TailScale(
        double limitX,
        double limitY,
        double slope,
        double power)
    {
        var scaledX = slope * limitX;
        var excess = Math.Pow(scaledX / limitY, power) - 1;
        return scaledX / Math.Pow(excess, 1 / power);
    }

    private static double PowerHyperbolic(double value, double power) =>
        value / Math.Pow(1 + Math.Pow(value, power), 1 / power);
}

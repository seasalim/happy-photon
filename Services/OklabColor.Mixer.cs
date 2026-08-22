using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal readonly record struct ColorMixerBandParameters(
    int Hue,
    int Saturation,
    int Luminance)
{
    internal static ColorMixerBandParameters From(
        ColorMixerBandSettings? settings) => settings == null
        ? default
        : new(settings.Hue, settings.Saturation, settings.Luminance);
}

internal readonly record struct ColorMixerParameters(
    ColorMixerBandParameters Red,
    ColorMixerBandParameters Orange,
    ColorMixerBandParameters Yellow,
    ColorMixerBandParameters Green,
    ColorMixerBandParameters Aqua,
    ColorMixerBandParameters Blue,
    ColorMixerBandParameters Purple,
    ColorMixerBandParameters Magenta,
    bool HasActive)
{
    internal static ColorMixerParameters From(ColorMixerSettings? settings) =>
        settings?.HasActivePixels == true
            ? new ColorMixerParameters(
                ColorMixerBandParameters.From(settings.Red),
                ColorMixerBandParameters.From(settings.Orange),
                ColorMixerBandParameters.From(settings.Yellow),
                ColorMixerBandParameters.From(settings.Green),
                ColorMixerBandParameters.From(settings.Aqua),
                ColorMixerBandParameters.From(settings.Blue),
                ColorMixerBandParameters.From(settings.Purple),
                ColorMixerBandParameters.From(settings.Magenta),
                HasActive: true)
            : default;

    internal ColorMixerBandParameters GetBand(int index) => index switch
    {
        0 => Red,
        1 => Orange,
        2 => Yellow,
        3 => Green,
        4 => Aqua,
        5 => Blue,
        6 => Purple,
        7 => Magenta,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}

internal static partial class OklabColor
{
    internal const int MixerBandCount = 8;
    internal const double MixerMaximumHueShiftDegrees = 30;
    internal const double MixerMaximumLightnessShift = 0.20;

    private static readonly double[] MixerBandCenters =
    [
        24 * Math.PI / 180,
        56 * Math.PI / 180,
        105 * Math.PI / 180,
        146 * Math.PI / 180,
        195 * Math.PI / 180,
        266 * Math.PI / 180,
        304 * Math.PI / 180,
        341 * Math.PI / 180
    ];

    internal static double GetMixerBandCenterRadians(int band) =>
        MixerBandCenters[band];

    internal static double MixerBandWeight(int band, double hueRadians)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(band);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            band,
            MixerBandCount);
        FindMixerSegment(
            NormalizeHue(hueRadians),
            out var left,
            out var right,
            out var leftWeight);
        if (band == left) return leftWeight;
        if (band == right) return 1 - leftWeight;
        return 0;
    }

    internal static double MixerHueReliability(double chroma) => SmoothStep(
        (chroma - HueReliabilityStart) /
        (HueReliabilityEnd - HueReliabilityStart));

    internal static Oklch ApplyChroma(
        Oklch source,
        int saturation,
        int vibrance,
        in ColorMixerParameters mixer)
    {
        if (!mixer.HasActive)
        {
            return ApplyChroma(source, saturation, vibrance);
        }

        var bandAdjusted = ApplyMixer(source, in mixer);
        var factor = vibrance == 0
            ? (100 + saturation) / 100.0
            : CombinedFactor(
                bandAdjusted.Chroma,
                bandAdjusted.HueRadians,
                saturation,
                vibrance);
        return bandAdjusted with
        {
            Chroma = bandAdjusted.Chroma * factor
        };
    }

    internal static OklabRgb TransformEncodedRec2020(
        OklabRgb encoded,
        int saturation,
        int vibrance,
        in ColorMixerParameters mixer)
    {
        if (!mixer.HasActive)
        {
            return TransformEncodedRec2020(encoded, saturation, vibrance);
        }
        if (encoded.Red == encoded.Green && encoded.Green == encoded.Blue)
        {
            return encoded;
        }

        var adjusted = ApplyChroma(
            FromEncodedRec2020(encoded),
            saturation,
            vibrance,
            in mixer);
        var linear = ProjectToRec2020Gamut(adjusted).LinearRec2020;
        return new OklabRgb(
            ToneLut.SrgbEncode(linear.Red),
            ToneLut.SrgbEncode(linear.Green),
            ToneLut.SrgbEncode(linear.Blue));
    }

    internal static OklabQ16 TransformQ16(
        ushort red,
        ushort green,
        ushort blue,
        int saturation,
        int vibrance,
        in ColorMixerParameters mixer)
    {
        if (!mixer.HasActive)
        {
            return TransformQ16(red, green, blue, saturation, vibrance);
        }
        if (red == green && green == blue)
        {
            return new OklabQ16(red, green, blue);
        }

        var decode = SrgbDecode.Value;
        var linearRed = decode[red];
        var linearGreen = decode[green];
        var linearBlue = decode[blue];
        var lPrime = Math.Cbrt(
            Rl * linearRed + Gl * linearGreen + Bl * linearBlue);
        var mPrime = Math.Cbrt(
            Rm * linearRed + Gm * linearGreen + Bm * linearBlue);
        var sPrime = Math.Cbrt(
            Rs * linearRed + Gs * linearGreen + Bs * linearBlue);
        var lightness = Ll * lPrime + Lm * mPrime + Ls * sPrime;
        var a = Al * lPrime + Am * mPrime + As * sPrime;
        var b = Abl * lPrime + Abm * mPrime + Abs * sPrime;
        var chroma = Math.Sqrt(a * a + b * b);
        var hue = Math.Atan2(b, a);
        if (hue < 0) hue += Math.Tau;
        var adjusted = ApplyChroma(
            new Oklch(lightness, chroma, hue),
            saturation,
            vibrance,
            in mixer);
        var (sinHue, cosHue) = Math.SinCos(adjusted.HueRadians);
        a = adjusted.Chroma * cosHue;
        b = adjusted.Chroma * sinHue;
        OklabRgb transformed;
        if (a == 0 && b == 0)
        {
            var neutral = adjusted.Lightness * adjusted.Lightness *
                adjusted.Lightness;
            transformed = new OklabRgb(neutral, neutral, neutral);
        }
        else
        {
            transformed = ProjectCartesianToRec2020Gamut(
                adjusted.Lightness,
                a,
                b);
        }
        var encode = SrgbEncode.Value;
        return new OklabQ16(
            EncodeQ16(transformed.Red, encode),
            EncodeQ16(transformed.Green, encode),
            EncodeQ16(transformed.Blue, encode));
    }

    private static Oklch ApplyMixer(
        Oklch source,
        in ColorMixerParameters mixer)
    {
        var reliability = MixerHueReliability(source.Chroma);
        if (reliability == 0)
        {
            return source;
        }

        FindMixerSegment(
            NormalizeHue(source.HueRadians),
            out var left,
            out var right,
            out var leftWeight);
        var rightWeight = 1 - leftWeight;
        var leftBand = mixer.GetBand(left);
        var rightBand = mixer.GetBand(right);
        var hueOffset = Weighted(
            leftBand.Hue,
            rightBand.Hue,
            leftWeight,
            rightWeight);
        var saturationOffset = Weighted(
            leftBand.Saturation,
            rightBand.Saturation,
            leftWeight,
            rightWeight);
        var luminanceOffset = Weighted(
            leftBand.Luminance,
            rightBand.Luminance,
            leftWeight,
            rightWeight);

        return new Oklch(
            Math.Clamp(
                source.Lightness + reliability * luminanceOffset / 100 *
                    MixerMaximumLightnessShift,
                0,
                1),
            source.Chroma *
                (1 + reliability * saturationOffset / 100),
            NormalizeHue(
                source.HueRadians + reliability * hueOffset / 100 *
                    MixerMaximumHueShiftDegrees * Math.PI / 180));
    }

    private static double Weighted(
        int left,
        int right,
        double leftWeight,
        double rightWeight) =>
        left * leftWeight + right * rightWeight;

    private static void FindMixerSegment(
        double hue,
        out int left,
        out int right,
        out double leftWeight)
    {
        for (var index = 0; index < MixerBandCount; index++)
        {
            var start = MixerBandCenters[index];
            var end = index == MixerBandCount - 1
                ? Math.Tau
                : MixerBandCenters[index + 1];
            if (hue < start || hue >= end)
            {
                continue;
            }

            left = index;
            right = (index + 1) % MixerBandCount;
            var position = (hue - start) / (end - start);
            leftWeight = 0.5 * (1 + Math.Cos(Math.PI * position));
            return;
        }

        left = MixerBandCount - 1;
        right = 0;
        var wrappedHue = hue < MixerBandCenters[0]
            ? hue + Math.Tau
            : hue;
        var wrappedEnd = Math.Tau + MixerBandCenters[0];
        var wrappedPosition = (wrappedHue - MixerBandCenters[left]) /
            (wrappedEnd - MixerBandCenters[left]);
        leftWeight = 0.5 * (1 + Math.Cos(Math.PI * wrappedPosition));
    }

    private static double NormalizeHue(double hueRadians)
    {
        var normalized = hueRadians % Math.Tau;
        return normalized < 0 ? normalized + Math.Tau : normalized;
    }
}

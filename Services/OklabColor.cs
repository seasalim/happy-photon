using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

internal readonly record struct OklabRgb(
    double Red,
    double Green,
    double Blue);

internal readonly record struct Oklch(
    double Lightness,
    double Chroma,
    double HueRadians);

internal readonly record struct OklabGamutResult(
    OklabRgb LinearRec2020,
    Oklch Oklch,
    bool WasProjected);

internal readonly record struct OklabQ16(
    ushort Red,
    ushort Green,
    ushort Blue);

internal static partial class OklabColor
{
    private const double SkinHueCenterRadians = 50 * Math.PI / 180;
    private const double SkinHueHalfWidthRadians = 40 * Math.PI / 180;
    private const double SkinDamping = 0.45;
    private const double HueReliabilityStart = 0.01;
    private const double HueReliabilityEnd = 0.04;
    private const double VibranceChromaTaper = 0.16;
    private const int ProjectionFallbackIterations = 26;
    private static readonly double SkinHueCenterCos =
        Math.Cos(SkinHueCenterRadians);
    private static readonly double SkinHueCenterSin =
        Math.Sin(SkinHueCenterRadians);
    private static readonly double SkinHueEdgeCos =
        Math.Cos(SkinHueHalfWidthRadians);
    private static readonly Lazy<double[]> SrgbDecode = new(
        () => CreateTransferLut(ToneLut.SrgbDecode));
    private static readonly Lazy<double[]> SrgbEncode = new(
        () => CreateTransferLut(ToneLut.SrgbEncode));

    // ITU-R BT.2020-2 Rec.2020 -> XYZ D65 composed with Ottosson's
    // XYZ D65 -> LMS matrix. The inverse below is derived from the same
    // authorities. https://bottosson.github.io/posts/oklab/
    private const double Rl = 0.6166884417511044;
    private const double Gl = 0.3601590704701176;
    private const double Bl = 0.0230432935209057;
    private const double Rm = 0.2651401961832023;
    private const double Gm = 0.6358564846985174;
    private const double Bm = 0.0990302335663959;
    private const double Rs = 0.1001506451032589;
    private const double Gs = 0.2040043234319267;
    private const double Bs = 0.6963246774370333;

    private const double Lr = 2.1401404110414077;
    private const double Mr = -1.2463559504758097;
    private const double Sr = 0.1064317259122409;
    private const double Lg = -0.8848324527970222;
    private const double Mg = 2.1631727207205325;
    private const double Sg = -0.2783615921300064;
    private const double Lb = -0.0485790580026382;
    private const double Mb = -0.4544909079675299;
    private const double Sb = 1.5023562946423799;

    // Ottosson LMS' -> OKLab and its independently inverted matrix.
    private const double Ll = 0.2104542553;
    private const double Lm = 0.7936177850;
    private const double Ls = -0.0040720468;
    private const double Al = 1.9779984951;
    private const double Am = -2.4285922050;
    private const double As = 0.4505937099;
    private const double Abl = 0.0259040371;
    private const double Abm = 0.7827717662;
    private const double Abs = -0.8086757660;

    private const double Llightness = 0.9999999984505197;
    private const double La = 0.3963377921737678;
    private const double LbInverse = 0.2158037580607588;
    private const double Mlightness = 1.0000000088817607;
    private const double Ma = -0.1055613423236563;
    private const double MbInverse = -0.0638541747717059;
    private const double Slightness = 1.0000000546724106;
    private const double Sa = -0.0894841820949657;
    private const double SbInverse = -1.2914855378640917;

    internal static double[,] Rec2020ToLmsMatrix => new[,]
    {
        { Rl, Gl, Bl },
        { Rm, Gm, Bm },
        { Rs, Gs, Bs }
    };

    internal static double[,] LmsToRec2020Matrix => new[,]
    {
        { Lr, Mr, Sr },
        { Lg, Mg, Sg },
        { Lb, Mb, Sb }
    };

    internal static Oklch FromEncodedRec2020(OklabRgb encoded) =>
        FromLinearRec2020(new OklabRgb(
            ToneLut.SrgbDecode(encoded.Red),
            ToneLut.SrgbDecode(encoded.Green),
            ToneLut.SrgbDecode(encoded.Blue)));

    internal static Oklch FromLinearRec2020(OklabRgb linear)
    {
        var lPrime = Math.Cbrt(
            Rl * linear.Red + Gl * linear.Green + Bl * linear.Blue);
        var mPrime = Math.Cbrt(
            Rm * linear.Red + Gm * linear.Green + Bm * linear.Blue);
        var sPrime = Math.Cbrt(
            Rs * linear.Red + Gs * linear.Green + Bs * linear.Blue);
        var lightness = Ll * lPrime + Lm * mPrime + Ls * sPrime;
        var a = Al * lPrime + Am * mPrime + As * sPrime;
        var b = Abl * lPrime + Abm * mPrime + Abs * sPrime;
        var chroma = Math.Sqrt(a * a + b * b);
        var hue = chroma == 0 ? 0 : Math.Atan2(b, a);
        if (hue < 0)
        {
            hue += Math.Tau;
        }
        return new Oklch(lightness, chroma, hue);
    }

    internal static Oklch ApplyChroma(
        Oklch source,
        int saturation,
        int vibrance) =>
        source with
        {
            Chroma = source.Chroma * CombinedFactor(
                source.Chroma,
                source.HueRadians,
                saturation,
                vibrance)
        };

    internal static double CombinedFactor(
        double chroma,
        double hueRadians,
        int saturation,
        int vibrance)
    {
        var saturationFactor = (100 + saturation) / 100.0;
        var vibranceFactor = 1 + vibrance / 100.0 * 0.5 *
            VibranceWeight(chroma, hueRadians);
        return saturationFactor * vibranceFactor;
    }

    internal static double VibranceWeight(
        double chroma,
        double hueRadians)
        => VibranceWeight(
            chroma,
            chroma * Math.Cos(hueRadians),
            chroma * Math.Sin(hueRadians));

    private static double VibranceWeight(
        double chroma,
        double a,
        double b)
    {
        if (chroma <= 0)
        {
            return 1;
        }

        var normalized = chroma / VibranceChromaTaper;
        var chromaWeight = 1 / (1 + normalized * normalized);
        var reliability = SmoothStep(
            (chroma - HueReliabilityStart) /
            (HueReliabilityEnd - HueReliabilityStart));
        var dot = (a * SkinHueCenterCos + b * SkinHueCenterSin) / chroma;
        var skinWindow = SmoothStep(
            (dot - SkinHueEdgeCos) / (1 - SkinHueEdgeCos));
        return chromaWeight *
            (1 - SkinDamping * reliability * skinWindow);
    }

    internal static OklabRgb ToLinearRec2020(Oklch color)
    {
        if (color.Chroma == 0)
        {
            var neutral = color.Lightness * color.Lightness * color.Lightness;
            return new OklabRgb(neutral, neutral, neutral);
        }

        var a = color.Chroma * Math.Cos(color.HueRadians);
        var b = color.Chroma * Math.Sin(color.HueRadians);
        var lPrime = Llightness * color.Lightness + La * a + LbInverse * b;
        var mPrime = Mlightness * color.Lightness + Ma * a + MbInverse * b;
        var sPrime = Slightness * color.Lightness + Sa * a + SbInverse * b;
        var l = lPrime * lPrime * lPrime;
        var m = mPrime * mPrime * mPrime;
        var s = sPrime * sPrime * sPrime;
        return new OklabRgb(
            Lr * l + Mr * m + Sr * s,
            Lg * l + Mg * m + Sg * s,
            Lb * l + Mb * m + Sb * s);
    }

    internal static OklabRgb TransformEncodedRec2020(
        OklabRgb encoded,
        int saturation,
        int vibrance)
    {
        if (encoded.Red == encoded.Green && encoded.Green == encoded.Blue)
        {
            return encoded;
        }

        var adjusted = ApplyChroma(
            FromEncodedRec2020(encoded),
            saturation,
            vibrance);
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
        int vibrance)
    {
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
        var saturationFactor = (100 + saturation) / 100.0;
        var vibranceFactor = 1.0;
        if (vibrance != 0)
        {
            var chroma = Math.Sqrt(a * a + b * b);
            vibranceFactor += vibrance / 100.0 * 0.5 *
                VibranceWeight(chroma, a, b);
        }
        var factor = saturationFactor * vibranceFactor;
        a *= factor;
        b *= factor;
        OklabRgb transformed;
        if (a == 0 && b == 0)
        {
            var neutral = lightness * lightness * lightness;
            transformed = new OklabRgb(neutral, neutral, neutral);
        }
        else
        {
            transformed = ProjectCartesianToRec2020Gamut(lightness, a, b);
        }
        var encode = SrgbEncode.Value;
        return new OklabQ16(
            EncodeQ16(transformed.Red, encode),
            EncodeQ16(transformed.Green, encode),
            EncodeQ16(transformed.Blue, encode));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsInGamut(OklabRgb value) =>
        value.Red is >= 0 and <= 1 &&
        value.Green is >= 0 and <= 1 &&
        value.Blue is >= 0 and <= 1;

    private static double[] CreateTransferLut(Func<double, double> transfer)
    {
        var result = new double[ushort.MaxValue + 1];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = transfer(index / (double)ushort.MaxValue);
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort EncodeQ16(double linear, double[] encode)
    {
        if (linear <= 0) return ushort.MinValue;
        if (linear >= 1) return ushort.MaxValue;
        var position = linear * ushort.MaxValue;
        var lower = (int)position;
        var fraction = position - lower;
        var encoded = encode[lower] +
            (encode[lower + 1] - encode[lower]) * fraction;
        return (ushort)Math.Round(
            encoded * ushort.MaxValue,
            MidpointRounding.AwayFromZero);
    }

    private static double SmoothStep(double value)
    {
        var bounded = Math.Clamp(value, 0, 1);
        return bounded * bounded * (3 - 2 * bounded);
    }

}

namespace HappyPhoton.Services;

internal static class RgbColorSpaceMatrices
{
    // W3C CSS Color 4 published values, rounded to seven decimal places.
    internal static readonly double[,] LinearSrgbToXyzD65PublishedRounded =
    {
        { 0.4124564, 0.3575761, 0.1804375 },
        { 0.2126729, 0.7151522, 0.0721750 },
        { 0.0193339, 0.1191920, 0.9503041 }
    };

    // Derived directly from IEC 61966-2-1 primaries and D65.
    internal static readonly double[,] LinearSrgbToXyzD65DerivedExact =
    {
        { 0.4123907992659595, 0.3575843393838780, 0.1804807884018343 },
        { 0.2126390058715104, 0.7151686787677560, 0.0721923153607337 },
        { 0.0193308187155918, 0.1191947797946259, 0.9505321522496607 }
    };

    internal static readonly double[,] XyzD65ToLinearSrgbPublishedRounded =
    {
        { 3.2404542, -1.5371385, -0.4985314 },
        { -0.9692660, 1.8760108, 0.0415560 },
        { 0.0556434, -0.2040259, 1.0572252 }
    };

    internal static readonly double[,] LinearRec2020ToXyzD65DerivedExact =
    {
        { 0.6369580483012914, 0.1446169035862083, 0.1688809751641721 },
        {
            Rec2020Luminance.Red,
            Rec2020Luminance.Green,
            Rec2020Luminance.Blue
        },
        { 0.0000000000000000, 0.0280726930490874, 1.0609850577107910 }
    };

    internal static readonly double[,] XyzD65ToLinearRec2020DerivedExact =
    {
        { 1.7166511879712674, -0.3556707837763922, -0.2533662813736597 },
        { -0.6666843518324893, 1.6164812366349395, 0.0157685458139111 },
        { 0.0176398574453109, -0.0427706132578085, 0.9421031212354738 }
    };

    internal static readonly double[,] LinearSrgbToLinearRec2020DerivedExact =
        Multiply(
            XyzD65ToLinearRec2020DerivedExact,
            LinearSrgbToXyzD65DerivedExact);

    // WORKING_SPACE.md §2's published composite, rounded to ten decimals.
    // Its < 1.5e-10 maximum channel error is far below half a Q16 quantum.
    internal static readonly double[,] LinearRec2020ToLinearSrgb =
    {
        { 1.6604910021, -0.5876411388, -0.0728498633 },
        { -0.1245504745, 1.1328998971, -0.0083494226 },
        { -0.0181507634, -0.1005788980, 1.1187296614 }
    };

    internal static readonly double[,] LinearSrgbToLinearRec2020 =
    {
        { 0.6274038959, 0.3292830384, 0.0433130657 },
        { 0.0690972894, 0.9195403951, 0.0113623156 },
        { 0.0163914389, 0.0880133079, 0.8955952532 }
    };

    // Derived directly from SMPTE EG 432-1 primaries and D65.
    internal static readonly double[,] LinearDisplayP3ToXyzD65DerivedExact =
    {
        { 0.4865709486482162, 0.2656676931690931, 0.1982172852343625 },
        { 0.2289745640697488, 0.6917385218365064, 0.0792869140937450 },
        { 0.0000000000000000, 0.0451133818589026, 1.0439443689009760 }
    };

    // WORKING_SPACE.md §9's derived composite, rounded to ten decimal places.
    internal static readonly double[,] LinearRec2020ToLinearDisplayP3 =
    {
        { 1.3435782526, -0.2821796705, -0.0613985821 },
        { -0.0652974528, 1.0757879158, -0.0104904631 },
        { 0.0028217873, -0.0195984945, 1.0167767073 }
    };

    private static double[,] Multiply(double[,] left, double[,] right)
    {
        var result = new double[3, 3];
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        for (var index = 0; index < 3; index++)
        {
            result[row, column] += left[row, index] * right[index, column];
        }

        return result;
    }
}

internal static class Rec2020Luminance
{
    internal const double Red = 0.2627002120112671;
    internal const double Green = 0.6779980715188708;
    internal const double Blue = 0.0593017164698620;
}

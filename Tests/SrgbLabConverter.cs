namespace HappyPhoton.Tests;

internal static class SrgbLabConverter
{
    public static PrecisionLab ToLab(
        double red,
        double green,
        double blue,
        double[,] srgbToXyzD65)
    {
        var r = Decode(red);
        var g = Decode(green);
        var b = Decode(blue);
        var x = (srgbToXyzD65[0, 0] * r + srgbToXyzD65[0, 1] * g +
            srgbToXyzD65[0, 2] * b) / 0.95047;
        var y = srgbToXyzD65[1, 0] * r + srgbToXyzD65[1, 1] * g +
            srgbToXyzD65[1, 2] * b;
        var z = (srgbToXyzD65[2, 0] * r + srgbToXyzD65[2, 1] * g +
            srgbToXyzD65[2, 2] * b) / 1.08883;
        var fx = PivotXyz(x);
        var fy = PivotXyz(y);
        var fz = PivotXyz(z);
        return new PrecisionLab(
            116 * fy - 16,
            500 * (fx - fy),
            200 * (fy - fz));
    }

    private static double Decode(double value) =>
        value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);

    private static double PivotXyz(double value) =>
        value > 216.0 / 24389
            ? Math.Cbrt(value)
            : 841.0 / 108 * value + 4.0 / 29;
}

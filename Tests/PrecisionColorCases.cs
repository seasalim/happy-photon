using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class PrecisionColorCases
{
    internal static readonly double[,] SrgbToXyzD65 =
        RgbColorSpaceMatrices.LinearSrgbToXyzD65DerivedExact;

    internal static readonly double[,] Rec2020ToXyzD65 =
        RgbColorSpaceMatrices.LinearRec2020ToXyzD65DerivedExact;

    internal static readonly double[,] BradfordD65ToD50 =
    {
        { 1.0479298208405488, 0.0229467933410191, -0.0501922295431356 },
        { 0.0296278156881593, 0.9904344845732490, -0.0170738250293851 },
        { -0.0092430581525912, 0.0150551448965779, 0.7518742814281370 }
    };

    internal static readonly double[,] RommToXyzD50 =
    {
        { 0.7976749, 0.1351917, 0.0313534 },
        { 0.2880402, 0.7118741, 0.0000857 },
        { 0.0000000, 0.0000000, 0.8252100 }
    };

    internal static double[] Transform(double[,] matrix, double[] value) =>
    [
        matrix[0, 0] * value[0] + matrix[0, 1] * value[1] + matrix[0, 2] * value[2],
        matrix[1, 0] * value[0] + matrix[1, 1] * value[1] + matrix[1, 2] * value[2],
        matrix[2, 0] * value[0] + matrix[2, 1] * value[1] + matrix[2, 2] * value[2]
    ];

    internal static double[,] Multiply(double[,] left, double[,] right)
    {
        var result = new double[3, 3];
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        for (var inner = 0; inner < 3; inner++)
        {
            result[row, column] += left[row, inner] * right[inner, column];
        }
        return result;
    }

    internal static double[,] Invert(double[,] value)
    {
        var a = value[0, 0]; var b = value[0, 1]; var c = value[0, 2];
        var d = value[1, 0]; var e = value[1, 1]; var f = value[1, 2];
        var g = value[2, 0]; var h = value[2, 1]; var i = value[2, 2];
        var determinant = a * (e * i - f * h) - b * (d * i - f * g) +
            c * (d * h - e * g);
        return new[,]
        {
            { (e * i - f * h) / determinant, (c * h - b * i) / determinant, (b * f - c * e) / determinant },
            { (f * g - d * i) / determinant, (a * i - c * g) / determinant, (c * d - a * f) / determinant },
            { (d * h - e * g) / determinant, (b * g - a * h) / determinant, (a * e - b * d) / determinant }
        };
    }
}

public sealed class PrecisionColorCasesTests
{
    [Fact]
    public void PublishedSrgbMatrix_AgreesWithDerivationAndOracle() =>
        ColorScienceMatrixAssertions.AssertPublishedAndOracle(
            PrecisionColorCases.SrgbToXyzD65,
            "linear-srgb-d65",
            2e-12);

    [Fact]
    public void PublishedSrgbToRec2020RedVector_IsPinned()
    {
        var conversion = PrecisionColorCases.Multiply(
            PrecisionColorCases.Invert(PrecisionColorCases.Rec2020ToXyzD65),
            PrecisionColorCases.SrgbToXyzD65);

        var actual = PrecisionColorCases.Transform(conversion, [1, 0, 0]);
        Assert.Equal(0.627403896, actual[0], 8);
        Assert.Equal(0.069097289, actual[1], 8);
        Assert.Equal(0.016391439, actual[2], 8);
    }

    [Fact]
    public void PublishedBradfordD65WhiteToD50White_IsPinned()
    {
        var actual = PrecisionColorCases.Transform(
            PrecisionColorCases.BradfordD65ToD50,
            [0.9504559271, 1.0, 1.0890577508]);

        Assert.Equal(0.964295666, actual[0], 8);
        Assert.Equal(1.000000036, actual[1], 8);
        Assert.Equal(0.825104539, actual[2], 8);
    }

    [Fact]
    public void PublishedLinearRommRedPrimary_IsPinned()
    {
        var actual = PrecisionColorCases.Transform(
            PrecisionColorCases.RommToXyzD50,
            [1, 0, 0]);

        Assert.Equal(0.7976749, actual[0], 7);
        Assert.Equal(0.2880402, actual[1], 7);
        Assert.Equal(0, actual[2], 7);
    }
}

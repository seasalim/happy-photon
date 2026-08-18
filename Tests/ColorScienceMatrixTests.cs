using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class ColorScienceMatrixAssertions
{
    // Published chromaticities and matrices: W3C CSS Color 4 sample conversions,
    // sourced in turn from IEC 61966-2-1, ITU-R BT.2020-2, and ISO 22028-2.
    // https://www.w3.org/TR/css-color-4/conversions.js
    public static void AssertPublishedAndOracle(
        double[,] published,
        string oracleId,
        double tolerance)
    {
        var oracle = ColorScienceOracleData.Load().Space(oracleId);
        var derived = DeriveRgbToXyz(oracle.Primaries, oracle.WhitePoint);
        AssertMatrixClose(published, derived, tolerance, "published vs derived");
        AssertMatrixClose(published, ToMatrix(oracle.MatrixRgbToXyz), tolerance,
            "published vs colour-science");
        AssertMatrixClose(derived, ToMatrix(oracle.MatrixRgbToXyz), tolerance,
            "derived vs colour-science");
    }

    public static double[,] DeriveRgbToXyz(double[][] primaries, double[] whitePoint)
    {
        Assert.Equal(3, primaries.Length);
        var primaryMatrix = new double[3, 3];
        for (var column = 0; column < 3; column++)
        {
            var x = primaries[column][0];
            var y = primaries[column][1];
            primaryMatrix[0, column] = x / y;
            primaryMatrix[1, column] = 1;
            primaryMatrix[2, column] = (1 - x - y) / y;
        }

        var white = XyToXyz(whitePoint);
        var scales = PrecisionColorCases.Transform(
            PrecisionColorCases.Invert(primaryMatrix), white);
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            primaryMatrix[row, column] *= scales[column];
        }
        return primaryMatrix;
    }

    public static double[,] ToMatrix(double[][] values)
    {
        Assert.Equal(3, values.Length);
        Assert.All(values, row => Assert.Equal(3, row.Length));
        var result = new double[3, 3];
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            result[row, column] = values[row][column];
        }
        return result;
    }

    public static void AssertMatrixClose(
        double[,] actual,
        double[,] expected,
        double tolerance,
        string comparison)
    {
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            Assert.True(
                Math.Abs(actual[row, column] - expected[row, column]) <= tolerance,
                $"{comparison} [{row},{column}]: expected {expected[row, column]:R}, " +
                $"actual {actual[row, column]:R}, tolerance {tolerance:R}.");
        }
    }

    public static double[] XyToXyz(double[] xy) =>
        [xy[0] / xy[1], 1, (1 - xy[0] - xy[1]) / xy[1]];
}

public sealed class ColorScienceMatrixTests
{
    [Fact]
    public void SrgbAuthority_ExposesBothDeliberateNumericVariants()
    {
        ColorScienceMatrixAssertions.AssertPublishedAndOracle(
            RgbColorSpaceMatrices.LinearSrgbToXyzD65PublishedRounded,
            "linear-srgb-d65",
            2.5e-4);
        ColorScienceMatrixAssertions.AssertPublishedAndOracle(
            RgbColorSpaceMatrices.LinearSrgbToXyzD65DerivedExact,
            "linear-srgb-d65",
            2e-12);
        Assert.NotEqual(
            RgbColorSpaceMatrices.LinearSrgbToXyzD65PublishedRounded[0, 0],
            RgbColorSpaceMatrices.LinearSrgbToXyzD65DerivedExact[0, 0]);
    }

    [Fact]
    public void ExistingCallSites_KeepTheirPriorSrgbVariant()
    {
        Assert.Same(
            RgbColorSpaceMatrices.LinearSrgbToXyzD65PublishedRounded,
            GoldenImageComparer.SrgbToXyzD65);
        Assert.Same(
            RgbColorSpaceMatrices.LinearSrgbToXyzD65PublishedRounded,
            PrecisionDeltaE.SrgbToXyzD65);
        Assert.Same(
            RgbColorSpaceMatrices.LinearSrgbToXyzD65DerivedExact,
            PrecisionColorCases.SrgbToXyzD65);
    }

    [Fact]
    public void Rec2020PublishedMatrix_AgreesWithDerivationAndOracle() =>
        ColorScienceMatrixAssertions.AssertPublishedAndOracle(
            PrecisionColorCases.Rec2020ToXyzD65,
            "linear-rec2020-d65",
            2e-12);

    [Fact]
    public void RommPublishedMatrix_AgreesWithDerivationAndOracle() =>
        ColorScienceMatrixAssertions.AssertPublishedAndOracle(
            PrecisionColorCases.RommToXyzD50,
            "linear-romm-d50",
            1.2e-4);

    [Fact]
    public void ProductionSrgbBasisVectors_AgreeWithOracle()
    {
        var oracle = ColorScienceOracleData.Load().Space("linear-srgb-d65");
        var toXyz = ColorScienceMatrixAssertions.ToMatrix(oracle.MatrixRgbToXyz);
        var fromXyz = ColorScienceMatrixAssertions.ToMatrix(oracle.MatrixXyzToRgb);
        for (var channel = 0; channel < 3; channel++)
        {
            var basis = new double[3];
            basis[channel] = 1;
            AssertVectorClose(
                ChromaticAdaptation.LinearSrgbToXyz(basis),
                PrecisionColorCases.Transform(toXyz, basis),
                2.5e-4);
            AssertVectorClose(
                ChromaticAdaptation.XyzToLinearSrgb(basis),
                PrecisionColorCases.Transform(fromXyz, basis),
                8e-4);
        }
    }

    [Fact]
    public void ProductionRec2020BasisVectors_AgreeWithOracle()
    {
        var oracle = ColorScienceOracleData.Load().Space("linear-rec2020-d65");
        var toXyz = ColorScienceMatrixAssertions.ToMatrix(oracle.MatrixRgbToXyz);
        var fromXyz = ColorScienceMatrixAssertions.ToMatrix(oracle.MatrixXyzToRgb);
        for (var channel = 0; channel < 3; channel++)
        {
            var basis = new double[3];
            basis[channel] = 1;
            AssertVectorClose(
                ChromaticAdaptation.LinearRec2020ToXyz(basis),
                PrecisionColorCases.Transform(toXyz, basis),
                2e-12);
            AssertVectorClose(
                ChromaticAdaptation.XyzToLinearRec2020(basis),
                PrecisionColorCases.Transform(fromXyz, basis),
                2e-12);
        }
    }

    private static void AssertVectorClose(double[] actual, double[] expected, double tolerance)
    {
        for (var index = 0; index < 3; index++)
        {
            Assert.InRange(actual[index], expected[index] - tolerance,
                expected[index] + tolerance);
        }
    }
}

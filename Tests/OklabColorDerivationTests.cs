using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class OklabColorDerivationTests
{
    private static readonly double[,] OttossonXyzToLms =
    {
        { 0.8189330101, 0.3618667424, -0.1288597137 },
        { 0.0329845436, 0.9293118715, 0.0361456387 },
        { 0.0482003018, 0.2643662691, 0.6338517070 }
    };

    [Fact]
    public void Rec2020LmsMatrices_AreIndependentlyDerivedAndMatchOracle()
    {
        var oracle = ColorScienceOracleData.Load();
        var rec2020 = oracle.Space("linear-rec2020-d65");
        var rec2020ToXyz = ColorScienceMatrixAssertions.DeriveRgbToXyz(
            rec2020.Primaries,
            rec2020.WhitePoint);
        var expectedForward = PrecisionColorCases.Multiply(
            OttossonXyzToLms,
            rec2020ToXyz);
        var expectedInverse = PrecisionColorCases.Invert(expectedForward);

        ColorScienceMatrixAssertions.AssertMatrixClose(
            OklabColor.Rec2020ToLmsMatrix,
            expectedForward,
            2e-15,
            "production forward vs BT.2020/Ottosson derivation");
        ColorScienceMatrixAssertions.AssertMatrixClose(
            OklabColor.LmsToRec2020Matrix,
            expectedInverse,
            2e-15,
            "production inverse vs BT.2020/Ottosson derivation");
        ColorScienceMatrixAssertions.AssertMatrixClose(
            OklabColor.Rec2020ToLmsMatrix,
            ColorScienceMatrixAssertions.ToMatrix(
                oracle.Oklab.MatrixRec2020ToLms),
            2e-15,
            "production forward vs colour-science oracle");
        ColorScienceMatrixAssertions.AssertMatrixClose(
            OklabColor.LmsToRec2020Matrix,
            ColorScienceMatrixAssertions.ToMatrix(
                oracle.Oklab.MatrixLmsToRec2020),
            2e-15,
            "production inverse vs colour-science oracle");
    }

    [Fact]
    public void Rec2020OklabRoundTrips_MatchCommittedOracle()
    {
        foreach (var vector in ColorScienceOracleData.Load().Oklab.RgbVectors)
        {
            var encoded = Rgb(vector.EncodedRec2020);
            var actual = OklabColor.FromEncodedRec2020(encoded);
            AssertClose(actual.Lightness, vector.Oklch[0], 2e-15);
            AssertClose(actual.Chroma, vector.Oklch[1], 2e-15);
            AssertAngleClose(actual.HueRadians, vector.Oklch[2], 1e-11);

            var recovered = OklabColor.ToLinearRec2020(actual);
            AssertRgbClose(recovered, vector.RecoveredLinearRec2020, 8e-15);
        }
    }

    [Fact]
    public void ConstantLightnessHueProjection_MatchesCommittedOracle()
    {
        foreach (var vector in ColorScienceOracleData.Load()
            .Oklab.GamutProjectionVectors)
        {
            var target = Lch(vector.TargetOklch);
            var unprojected = OklabColor.ToLinearRec2020(target);
            var actual = OklabColor.ProjectToRec2020Gamut(target);

            Assert.False(OklabColor.IsInGamut(unprojected));
            Assert.True(actual.WasProjected);
            Assert.Equal(target.Lightness, actual.Oklch.Lightness);
            Assert.Equal(target.HueRadians, actual.Oklch.HueRadians);
            AssertClose(actual.Oklch.Chroma, vector.ProjectedOklch[1], 5e-8);
            AssertRgbClose(actual.LinearRec2020,
                vector.ProjectedLinearRec2020, 1.5e-7);
        }
    }

    private static OklabRgb Rgb(double[] value) =>
        new(value[0], value[1], value[2]);

    private static Oklch Lch(double[] value) =>
        new(value[0], value[1], value[2]);

    private static void AssertRgbClose(
        OklabRgb actual,
        double[] expected,
        double tolerance)
    {
        AssertClose(actual.Red, expected[0], tolerance);
        AssertClose(actual.Green, expected[1], tolerance);
        AssertClose(actual.Blue, expected[2], tolerance);
    }

    private static void AssertClose(
        double actual,
        double expected,
        double tolerance) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);

    private static void AssertAngleClose(
        double actual,
        double expected,
        double tolerance)
    {
        var difference = Math.Abs(actual - expected) % Math.Tau;
        Assert.True(
            Math.Min(difference, Math.Tau - difference) <= tolerance,
            $"Expected angle {expected:R}, actual {actual:R}.");
    }
}

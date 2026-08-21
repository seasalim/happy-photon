using Xunit;

namespace HappyPhoton.Tests;

public sealed class ColorScienceOracleTests
{
    [Fact]
    public void Oracle_HasPinnedGeneratorAndRequiredDomains()
    {
        var oracle = ColorScienceOracleData.Load();

        Assert.Equal(1, oracle.SchemaVersion);
        Assert.Equal("0.4.7", oracle.Generator.ColourScienceVersion);
        Assert.Equal("2.4.4", oracle.Generator.NumpyVersion);
        Assert.Equal(
            ["linear-srgb-d65", "linear-rec2020-d65", "linear-romm-d50"],
            oracle.Spaces.Select(space => space.Id));
        Assert.Equal(2, oracle.Adaptations.Count);
        Assert.Equal(6, oracle.TransferFunctions.SrgbEotf.Count);
        Assert.Equal(5, oracle.Oklab.RgbVectors.Count);
        Assert.Equal(3, oracle.Oklab.GamutProjectionVectors.Count);
        Assert.Equal("ColorChecker24 - Before November 2014",
            oracle.ColorChecker.Dataset);
        Assert.Equal("CIE 1931 2 Degree Standard Observer",
            oracle.ColorChecker.Observer);
        Assert.Equal(24, oracle.ColorChecker.Patches.Count);
    }

    [Fact]
    public void RgbXyzRoundTrips_MatchCommittedVectors()
    {
        foreach (var space in ColorScienceOracleData.Load().Spaces)
        {
            var toXyz = ColorScienceMatrixAssertions.ToMatrix(space.MatrixRgbToXyz);
            var fromXyz = ColorScienceMatrixAssertions.ToMatrix(space.MatrixXyzToRgb);
            foreach (var sample in space.RoundTrips)
            {
                AssertVector(
                    PrecisionColorCases.Transform(toXyz, sample.Rgb),
                    sample.Xyz,
                    2e-12);
                AssertVector(
                    PrecisionColorCases.Transform(fromXyz, sample.Xyz),
                    sample.RecoveredRgb,
                    2e-12);
                AssertVector(sample.RecoveredRgb, sample.Rgb, 2e-12);
            }
        }
    }

    [Fact]
    public void BradfordAdaptation_MatchesCommittedWhiteVectors()
    {
        foreach (var adaptation in ColorScienceOracleData.Load().Adaptations)
        {
            var actual = PrecisionColorCases.Transform(
                ColorScienceMatrixAssertions.ToMatrix(adaptation.Matrix),
                adaptation.SourceWhiteXyz);
            AssertVector(actual, adaptation.AdaptedWhiteXyz, 2e-12);
            AssertVector(
                actual,
                ColorScienceMatrixAssertions.XyToXyz(adaptation.DestinationWhite),
                2e-7);
        }
    }

    [Fact]
    public void SrgbEotf_MatchesCommittedVectors()
    {
        foreach (var sample in ColorScienceOracleData.Load().TransferFunctions.SrgbEotf)
        {
            var actual = sample.Encoded <= 0.04045
                ? sample.Encoded / 12.92
                : Math.Pow((sample.Encoded + 0.055) / 1.055, 2.4);
            Assert.Equal(sample.Linear, actual, 14);
        }
    }

    private static void AssertVector(double[] actual, double[] expected, double tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < actual.Length; index++)
        {
            Assert.InRange(actual[index], expected[index] - tolerance,
                expected[index] + tolerance);
        }
    }
}

using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class OklabColorPropertyTests
{
    [Fact]
    public void Saturation_ScalesChromaAtConstantLightnessAndHue()
    {
        var random = new Random(162);
        for (var draw = 0; draw < 1_000; draw++)
        {
            var source = new Oklch(
                0.05 + random.NextDouble() * 0.90,
                0.001 + random.NextDouble() * 0.20,
                random.NextDouble() * Math.Tau);
            var saturation = random.Next(-100, 101);
            var adjusted = OklabColor.ApplyChroma(
                source,
                saturation,
                vibrance: 0);

            Assert.Equal(source.Lightness, adjusted.Lightness);
            Assert.Equal(source.HueRadians, adjusted.HueRadians);
            Assert.InRange(
                Math.Abs(
                    adjusted.Chroma / source.Chroma -
                    (100 + saturation) / 100.0),
                0,
                2e-15);
        }
    }

    [Fact]
    public void SaturationMinus100_IsExactAchromaticOklch()
    {
        var random = new Random(1162);
        for (var draw = 0; draw < 500; draw++)
        {
            var source = new Oklch(
                random.NextDouble(),
                random.NextDouble() * 0.5,
                random.NextDouble() * Math.Tau);

            var adjusted = OklabColor.ApplyChroma(
                source,
                saturation: -100,
                vibrance: random.Next(-100, 101));

            Assert.Equal(0, adjusted.Chroma);
            Assert.Equal(source.Lightness, adjusted.Lightness);
            Assert.Equal(source.HueRadians, adjusted.HueRadians);
        }
    }

    [Fact]
    public void VibranceWeight_IsSignSymmetricMonotoneAndBounded()
    {
        foreach (var hue in new[] { 0.0, 50 * Math.PI / 180, Math.PI, Math.Tau })
        {
            var prior = double.PositiveInfinity;
            for (var step = 0; step <= 500; step++)
            {
                var chroma = step / 1_000.0;
                var weight = OklabColor.VibranceWeight(chroma, hue);
                Assert.InRange(weight, 0, 1);
                Assert.True(weight <= prior + 2e-15,
                    $"Vibrance taper increased at C={chroma:R}, h={hue:R}.");
                prior = weight;

                var positive = OklabColor.CombinedFactor(
                    chroma, hue, saturation: 0, vibrance: 73);
                var negative = OklabColor.CombinedFactor(
                    chroma, hue, saturation: 0, vibrance: -73);
                Assert.InRange(Math.Abs(
                    (positive - 1) - (1 - negative)), 0, 2e-15);
            }
        }
    }

    [Fact]
    public void SkinWindow_IsSmoothPeriodicAndUndefinedHueSafeAtZeroChroma()
    {
        const double chroma = 0.12;
        var skin = OklabColor.VibranceWeight(
            chroma,
            50 * Math.PI / 180);
        var redNeighbor = OklabColor.VibranceWeight(chroma, 0);
        var yellowNeighbor = OklabColor.VibranceWeight(
            chroma,
            100 * Math.PI / 180);
        Assert.True(skin < redNeighbor);
        Assert.True(skin < yellowNeighbor);

        foreach (var hue in new[] { -Math.Tau, -0.2, 0.0, 0.2, Math.Tau })
        {
            Assert.Equal(1, OklabColor.VibranceWeight(0, hue));
            Assert.InRange(Math.Abs(
                OklabColor.VibranceWeight(chroma, hue) -
                OklabColor.VibranceWeight(chroma, hue + Math.Tau)),
                0,
                2e-15);
        }

        var edge = 10 * Math.PI / 180;
        var outside = OklabColor.VibranceWeight(chroma, edge - 1e-8);
        var boundary = OklabColor.VibranceWeight(chroma, edge);
        var inside = OklabColor.VibranceWeight(chroma, edge + 1e-8);
        Assert.InRange(Math.Abs(outside - boundary), 0, 2e-14);
        Assert.InRange(Math.Abs(inside - boundary), 0, 2e-14);
    }

    [Fact]
    public void CombinedFactor_ComposesSaturationAndWeightedVibrance()
    {
        var source = new Oklch(0.62, 0.11, 0.8);
        var weight = OklabColor.VibranceWeight(
            source.Chroma,
            source.HueRadians);
        var expected = 1.37 * (1 - 0.42 * 0.5 * weight);

        var adjusted = OklabColor.ApplyChroma(
            source,
            saturation: 37,
            vibrance: -42);

        Assert.InRange(
            Math.Abs(adjusted.Chroma / source.Chroma - expected),
            0,
            2e-15);
    }

    [Fact]
    public void GamutProjection_IsMaximalAndNotChannelClamping()
    {
        var vector = ColorScienceOracleData.Load()
            .Oklab.GamutProjectionVectors[0];
        var target = new Oklch(
            vector.TargetOklch[0],
            vector.TargetOklch[1],
            vector.TargetOklch[2]);
        var result = OklabColor.ProjectToRec2020Gamut(target);
        var beyond = OklabColor.ToLinearRec2020(
            result.Oklch with { Chroma = result.Oklch.Chroma + 1e-6 });
        var clamped = new OklabRgb(
            Math.Clamp(vector.UnprojectedLinearRec2020[0], 0, 1),
            Math.Clamp(vector.UnprojectedLinearRec2020[1], 0, 1),
            Math.Clamp(vector.UnprojectedLinearRec2020[2], 0, 1));

        Assert.True(OklabColor.IsInGamut(result.LinearRec2020));
        Assert.False(OklabColor.IsInGamut(beyond));
        Assert.True(Distance(result.LinearRec2020, clamped) > 0.1);
    }

    [Fact]
    public void InGamutProjection_ReturnsInputUnchanged()
    {
        var source = OklabColor.FromEncodedRec2020(
            new OklabRgb(0.31, 0.42, 0.27));
        var linear = OklabColor.ToLinearRec2020(source);

        var result = OklabColor.ProjectToRec2020Gamut(source);

        Assert.False(result.WasProjected);
        Assert.Equal(source, result.Oklch);
        Assert.Equal(linear, result.LinearRec2020);
    }

    private static double Distance(OklabRgb first, OklabRgb second) =>
        Math.Sqrt(
            Math.Pow(first.Red - second.Red, 2) +
            Math.Pow(first.Green - second.Green, 2) +
            Math.Pow(first.Blue - second.Blue, 2));
}

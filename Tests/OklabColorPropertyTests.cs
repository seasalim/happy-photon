using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class OklabColorPropertyTests
{
    [Theory]
    [InlineData(ColorMixerBand.Red, 24)]
    [InlineData(ColorMixerBand.Orange, 56)]
    [InlineData(ColorMixerBand.Yellow, 105)]
    [InlineData(ColorMixerBand.Green, 146)]
    [InlineData(ColorMixerBand.Aqua, 195)]
    [InlineData(ColorMixerBand.Blue, 266)]
    [InlineData(ColorMixerBand.Purple, 304)]
    [InlineData(ColorMixerBand.Magenta, 341)]
    public void MixerBandCenters_MatchThemeSwatches(
        ColorMixerBand band,
        int expectedDegrees) =>
        Assert.Equal(
            expectedDegrees * Math.PI / 180,
            OklabColor.GetMixerBandCenterRadians((int)band),
            12);

    [Fact]
    public void MixerBands_FormSmoothPeriodicPartitionOfUnity()
    {
        for (var step = -720; step <= 1_440; step++)
        {
            var hue = step * Math.PI / 360;
            var sum = Enumerable.Range(0, OklabColor.MixerBandCount)
                .Sum(band => OklabColor.MixerBandWeight(band, hue));
            Assert.InRange(Math.Abs(sum - 1), 0, 2e-15);
            for (var band = 0; band < OklabColor.MixerBandCount; band++)
            {
                Assert.InRange(
                    Math.Abs(
                        OklabColor.MixerBandWeight(band, hue) -
                        OklabColor.MixerBandWeight(band, hue + Math.Tau)),
                    0,
                    1e-14);
            }
        }

        const double epsilon = 1e-8;
        for (var edge = 0; edge < OklabColor.MixerBandCount; edge++)
        {
            var hue = OklabColor.GetMixerBandCenterRadians(edge);
            for (var band = 0; band < OklabColor.MixerBandCount; band++)
            {
                var at = OklabColor.MixerBandWeight(band, hue);
                var before = OklabColor.MixerBandWeight(band, hue - epsilon);
                var after = OklabColor.MixerBandWeight(band, hue + epsilon);
                Assert.InRange(Math.Abs(before - at), 0, 2e-14);
                Assert.InRange(Math.Abs(after - at), 0, 2e-14);
            }
        }
    }

    [Fact]
    public void Mixer_AchromaticAndUnreliableHuesTakeNoBandEdits()
    {
        var mixer = ColorMixerParameters.From(CreateUniformMixer(
            hue: 100,
            saturation: 100,
            luminance: 100));
        foreach (var chroma in new[] { 0.0, 0.005, 0.01 })
        {
            var source = new Oklch(0.42, chroma, 1.7);
            Assert.Equal(
                source,
                OklabColor.ApplyChroma(
                    source,
                    saturation: 0,
                    vibrance: 0,
                    in mixer));
        }
    }

    [Fact]
    public void Mixer_InGamutHueRotationIsExactAtBandCenter()
    {
        var settings = new ColorMixerSettings();
        settings.Orange.Hue = 100;
        var mixer = ColorMixerParameters.From(settings);
        var source = new Oklch(
            0.60,
            0.05,
            OklabColor.GetMixerBandCenterRadians((int)ColorMixerBand.Orange));

        var adjusted = OklabColor.ApplyChroma(source, 0, 0, in mixer);

        Assert.Equal(source.Lightness, adjusted.Lightness);
        Assert.Equal(source.Chroma, adjusted.Chroma);
        Assert.InRange(
            AngleDistance(
                adjusted.HueRadians,
                source.HueRadians + 30 * Math.PI / 180),
            0,
            2e-15);
        Assert.True(OklabColor.IsInGamut(
            OklabColor.ToLinearRec2020(adjusted)));
        Assert.False(OklabColor.ProjectToRec2020Gamut(adjusted).WasProjected);
    }

    [Fact]
    public void Mixer_UniformSaturationEqualsGlobalOnReliableHues()
    {
        var random = new Random(181);
        for (var draw = 0; draw < 1_000; draw++)
        {
            var value = random.Next(-100, 101);
            var source = new Oklch(
                0.05 + random.NextDouble() * 0.90,
                0.04 + random.NextDouble() * 0.16,
                random.NextDouble() * Math.Tau);
            var mixer = ColorMixerParameters.From(CreateUniformMixer(
                hue: 0,
                saturation: value,
                luminance: 0));

            var bandAdjusted = OklabColor.ApplyChroma(source, 0, 0, in mixer);
            var globalAdjusted = OklabColor.ApplyChroma(source, value, 0);

            Assert.Equal(globalAdjusted.Lightness, bandAdjusted.Lightness);
            Assert.InRange(
                Math.Abs(globalAdjusted.Chroma - bandAdjusted.Chroma),
                0,
                1e-16);
            Assert.Equal(globalAdjusted.HueRadians, bandAdjusted.HueRadians);
        }
    }

    [Fact]
    public void Mixer_LuminanceIsMonotoneBoundedAndClampedBeforeProjection()
    {
        var hue = OklabColor.GetMixerBandCenterRadians(
            (int)ColorMixerBand.Blue);
        var values = new List<double>();
        for (var value = -100; value <= 100; value += 10)
        {
            var settings = new ColorMixerSettings();
            settings.Blue.Luminance = value;
            var mixer = ColorMixerParameters.From(settings);
            values.Add(OklabColor.ApplyChroma(
                new Oklch(0.5, 0.08, hue), 0, 0, in mixer).Lightness);
        }
        Assert.True(values.SequenceEqual(values.Order()));

        var dark = new ColorMixerSettings();
        dark.Blue.Luminance = -100;
        var darkMixer = ColorMixerParameters.From(dark);
        var light = new ColorMixerSettings();
        light.Blue.Luminance = 100;
        var lightMixer = ColorMixerParameters.From(light);
        Assert.Equal(0, OklabColor.ApplyChroma(
            new Oklch(0.05, 0.08, hue), 0, 0, in darkMixer).Lightness);
        Assert.Equal(1, OklabColor.ApplyChroma(
            new Oklch(0.95, 0.08, hue), 0, 0, in lightMixer).Lightness);
    }

    [Fact]
    public void MixerProjection_PreservesPostEditLightnessAndHue()
    {
        var settings = new ColorMixerSettings();
        settings.Magenta.Hue = 100;
        settings.Magenta.Saturation = 100;
        settings.Magenta.Luminance = 60;
        var mixer = ColorMixerParameters.From(settings);
        var source = new Oklch(
            0.72,
            0.32,
            OklabColor.GetMixerBandCenterRadians((int)ColorMixerBand.Magenta));
        var target = OklabColor.ApplyChroma(source, 45, 0, in mixer);

        var projected = OklabColor.ProjectToRec2020Gamut(target);

        Assert.True(projected.WasProjected);
        Assert.True(OklabColor.IsInGamut(projected.LinearRec2020));
        Assert.Equal(target.Lightness, projected.Oklch.Lightness);
        Assert.Equal(target.HueRadians, projected.Oklch.HueRadians);
        Assert.True(projected.Oklch.Chroma < target.Chroma);
    }

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

    private static ColorMixerSettings CreateUniformMixer(
        int hue,
        int saturation,
        int luminance)
    {
        var mixer = new ColorMixerSettings();
        foreach (var band in Enum.GetValues<ColorMixerBand>())
        {
            var values = mixer.GetBand(band);
            values.Hue = hue;
            values.Saturation = saturation;
            values.Luminance = luminance;
        }
        return mixer;
    }

    private static double AngleDistance(double first, double second)
    {
        var difference = Math.Abs(first - second) % Math.Tau;
        return Math.Min(difference, Math.Tau - difference);
    }
}

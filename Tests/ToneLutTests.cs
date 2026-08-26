using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public class ToneLutTests
{
    private static readonly CurveData IdentityCurve = new();

    [Fact]
    public void Compose_UsesTheRequired65536EntryAnalyticShape()
    {
        var lut = ToneLut.Compose(Identity()).Red;

        Assert.Equal(65536, lut.Length);
        Assert.Equal(ToneLut.Evaluate(Identity(), 0), lut[0]);
        Assert.Equal(ToneLut.Evaluate(Identity(), 1), lut[^1]);
        Assert.InRange(Math.Abs(lut[0]), 0, 1e-15);
        Assert.InRange(Math.Abs(1 - lut[^1]), 0, 1e-15);
    }

    [Fact]
    public void Compose_IdentityReproducesSrgbEncodeWithinOneEightBitLsb()
    {
        var lut = ToneLut.Compose(Identity()).Red;

        for (var i = 0; i < lut.Length; i++)
        {
            var input = i / 65535.0;
            Assert.Equal(ToneLut.Evaluate(Identity(), input), lut[i]);
            Assert.InRange(
                Math.Abs(ToneLut.SrgbEncode(input) - lut[i]),
                0,
                1e-12);
        }
    }

    [Fact]
    public void Compose_IdentityChannelsShareOneArray()
    {
        var luts = ToneLut.Compose(Identity() with
        {
            CurveRed = new CurveData(),
            CurveGreen = new CurveData(),
            CurveBlue = new CurveData()
        });

        Assert.Same(luts.Red, luts.Green);
        Assert.Same(luts.Red, luts.Blue);
    }

    [Fact]
    public void Compose_AppliesChannelBeforeComposite()
    {
        var channel = CreateCurve(0.35, 0.72);
        var composite = CreateCurve(0.65, 0.42);
        var parameters = Identity() with
        {
            Curve = composite,
            CurveRed = channel
        };
        const double input = 0.2;
        var upstream = ToneLut.SrgbEncode(input);
        var expected = ToneLut.EvaluateCurve(
            composite,
            ToneLut.EvaluateCurve(channel, upstream));
        var reverse = ToneLut.EvaluateCurve(
            channel,
            ToneLut.EvaluateCurve(composite, upstream));

        var actual = ToneLut.Evaluate(parameters, input, channel);

        Assert.Equal(expected, actual, 12);
        Assert.True(Math.Abs(actual - reverse) > 1e-3);
    }

    [Fact]
    public void Compose_NonIdentityChannelOnlyAllocatesItsOwnArray()
    {
        var luts = ToneLut.Compose(Identity() with
        {
            CurveRed = CreateCurve(0.5, 0.7)
        });

        Assert.NotSame(luts.Red, luts.Green);
        Assert.Same(luts.Green, luts.Blue);
    }

    [Fact]
    public void Compose_PreservesDecreasingChannelSegments()
    {
        var decreasing = new CurveData
        {
            Points =
            [
                new CurvePoint(0, 1),
                new CurvePoint(1, 0)
            ]
        };
        decreasing.BuildLookupTable();

        var luts = ToneLut.Compose(Identity() with { CurveRed = decreasing });

        Assert.True(luts.Red[0] > luts.Red[^1]);
        Assert.Same(luts.Green, luts.Blue);
    }

    [Fact]
    public void Compose_IsMonotoneAcrossSeededRandomValidSettings()
    {
        var random = new Random(0x4850_21);
        var monotoneCurvesUsed = 0;

        // Sixteen draws cover identity and generated curves while checking
        // 1,048,576 adjacent LUT pairs across the seeded parameter space.
        const int drawCount = 16;
        for (var draw = 0; draw < drawCount; draw++)
        {
            var curve = draw % 2 == 0 ? IdentityCurve : CreateMonotoneCurve(random);
            if (!curve.IsIdentity())
            {
                monotoneCurvesUsed++;
            }

            var parameters = new ToneParams(
                ExposureEv: NextDouble(random, -3, 3),
                Fold: NextDouble(random, 1, 4),
                Brightness: random.Next(-100, 101),
                Contrast: random.Next(-100, 101),
                Shadows: random.Next(-100, 101),
                Highlights: random.Next(-100, 101),
                BaseLookEnabled: random.Next(2) == 1,
                Curve: curve);

            var lut = ToneLut.Compose(parameters).Red;

            for (var i = 1; i < lut.Length; i++)
            {
                Assert.True(
                    lut[i] >= lut[i - 1],
                    $"Draw {draw}, entry {i}: {lut[i]} < {lut[i - 1]} for {parameters}");
            }
        }

        Assert.Equal(drawCount / 2, monotoneCurvesUsed);
    }

    [Fact]
    public void HighlightShoulder_IsC1ContinuousAtKnee()
    {
        const double knee = 0.45;
        const double epsilon = 1e-6;
        var atKnee = ToneLut.HighlightShoulder(knee, knee);
        var leftDerivative =
            (atKnee - ToneLut.HighlightShoulder(knee - epsilon, knee)) / epsilon;
        var rightDerivative =
            (ToneLut.HighlightShoulder(knee + epsilon, knee) - atKnee) / epsilon;

        Assert.Equal(knee, atKnee, 12);
        Assert.InRange(Math.Abs(leftDerivative - 1), 0, 1e-8);
        Assert.InRange(Math.Abs(rightDerivative - 1), 0, 1e-8);
        Assert.InRange(Math.Abs(leftDerivative - rightDerivative), 0, 1e-8);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.0031308, 0.040449936)]
    [InlineData(0.18, 0.46135612950044164)]
    public void SrgbEncode_IsPinned(double input, double expected)
    {
        Assert.Equal(expected, ToneLut.SrgbEncode(input), 12);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 2, 4)]
    [InlineData(-2, 4, 1)]
    public void ExposureGain_IsPinned(
        double exposure,
        double fold,
        double expected)
    {
        Assert.Equal(expected, ToneLut.ExposureGain(exposure, fold), 12);
    }

    [Theory]
    [InlineData(-100, 0.45)]
    [InlineData(-50, 0.725)]
    [InlineData(0, 1)]
    [InlineData(100, 1)]
    public void HighlightKnee_IsPinned(int highlights, double expected)
    {
        Assert.Equal(expected, ToneLut.HighlightKnee(highlights), 12);
    }

    [Theory]
    [InlineData(0.2, 0.45, 0.2)]
    [InlineData(0.45, 0.45, 0.45)]
    [InlineData(0.8, 0.45, 0.7593301800086836)]
    [InlineData(1.2, 1, 1)]
    public void HighlightShoulder_IsPinned(double input, double knee, double expected)
    {
        Assert.Equal(expected, ToneLut.HighlightShoulder(input, knee), 12);
    }

    [Theory]
    [InlineData(0, 0.012)]
    [InlineData(0.25, 0.17959375)]
    [InlineData(0.5, 0.49775)]
    [InlineData(1, 0.97)]
    public void BaseLook_IsPinned(double input, double expected)
    {
        Assert.Equal(expected, ToneLut.BaseLook(input), 12);
    }

    [Theory]
    [InlineData(0.2, -100, 0)]
    [InlineData(0.5, 0, 0.5)]
    [InlineData(0.8, 100, 1)]
    public void Brightness_IsPinned(double input, int brightness, double expected)
    {
        Assert.Equal(expected, ToneLut.ApplyBrightness(input, brightness), 12);
    }

    [Theory]
    [InlineData(0.25, -100, 0.4187700759417734)]
    [InlineData(0.5, 100, 0.5)]
    [InlineData(0.75, 100, 1)]
    public void Contrast_IsPinned(double input, int contrast, double expected)
    {
        var actual = ToneLut.ApplyContrast(input, ToneLut.ContrastSlope(contrast));
        Assert.Equal(expected, actual, 12);
    }

    [Theory]
    [InlineData(0, -100, 0)]
    [InlineData(0.25, 100, 0.2869140625)]
    [InlineData(0.5, -100, 0.478125)]
    [InlineData(1, 100, 1)]
    public void Shadows_IsPinned(double input, int shadows, double expected)
    {
        Assert.Equal(expected, ToneLut.ApplyShadows(input, shadows), 12);
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(0.5, 50, 0.51875)]
    [InlineData(0.9, 100, 1)]
    public void PositiveHighlights_IsPinned(double input, int highlights, double expected)
    {
        Assert.Equal(expected, ToneLut.ApplyPositiveHighlights(input, highlights), 12);
    }

    [Fact]
    public void UserCurve_UsesLinearInterpolationBetweenLookupEntries()
    {
        var curve = CreateMonotoneCurve(new Random(71));
        var table = curve.LookupTable;
        const int lower = 93;
        const double fraction = 0.375;
        var input = (lower + fraction) / 255;
        var expected = (table[lower] * (1 - fraction) + table[lower + 1] * fraction) / 255;

        Assert.Equal(table[0] / 255.0, ToneLut.EvaluateCurve(curve, 0), 12);
        Assert.Equal(expected, ToneLut.EvaluateCurve(curve, input), 12);
        Assert.Equal(table[^1] / 255.0, ToneLut.EvaluateCurve(curve, 1), 12);
    }

    [Fact]
    public void BrightnessAndContrastAtPositiveMaximumCreatePlateauWithoutFoldback()
    {
        var lut = ToneLut.Compose(Identity() with
        {
            Brightness = 100,
            Contrast = 100
        }).Red;
        var plateauStart = Array.IndexOf(lut, 1.0);

        Assert.InRange(plateauStart, 1, lut.Length - 2);
        Assert.All(lut[plateauStart..], value => Assert.Equal(1, value));
        Assert.True(lut.Zip(lut.Skip(1)).All(pair => pair.Second >= pair.First));
    }

    private static ToneParams Identity() => new(
        ExposureEv: 0,
        Fold: 1,
        Brightness: 0,
        Contrast: 0,
        Shadows: 0,
        Highlights: 0,
        BaseLookEnabled: false,
        Curve: IdentityCurve);

    private static CurveData CreateMonotoneCurve(Random random)
    {
        while (true)
        {
            var curve = new CurveData
            {
                Points =
                [
                    new CurvePoint(0, 0),
                    new CurvePoint(0.25, NextDouble(random, 0, 0.25)),
                    new CurvePoint(0.5, NextDouble(random, 0.25, 0.5)),
                    new CurvePoint(0.75, NextDouble(random, 0.5, 0.75)),
                    new CurvePoint(1, 1)
                ]
            };
            curve.BuildLookupTable();

            if (!curve.IsIdentity() &&
                curve.LookupTable.Zip(curve.LookupTable.Skip(1))
                    .All(pair => pair.Second >= pair.First))
            {
                return curve;
            }
        }
    }

    private static CurveData CreateCurve(double x, double y)
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(x, y);
        return curve;
    }

    private static double NextDouble(Random random, double minimum, double maximum) =>
        minimum + random.NextDouble() * (maximum - minimum);
}

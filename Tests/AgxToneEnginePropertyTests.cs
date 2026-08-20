using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgxToneEnginePropertyTests
{
    private static readonly int[] SliderGrid = [-100, -50, 0, 50, 100];

    [Fact]
    public void Sigmoid_IsStrictlyMonotoneAcrossSliderGrid()
    {
        foreach (var contrast in SliderGrid)
        foreach (var highlights in SliderGrid)
        foreach (var shadows in SliderGrid)
        {
            var previous = AgxToneEngine.EvaluateSigmoid(
                0,
                AgxToneEngine.Slope(contrast),
                AgxToneEngine.ToePower(shadows),
                AgxToneEngine.ShoulderPower(highlights));
            for (var sample = 1; sample <= 4096; sample++)
            {
                var x = sample / 4096.0;
                var current = AgxToneEngine.EvaluateSigmoid(
                    x,
                    AgxToneEngine.Slope(contrast),
                    AgxToneEngine.ToePower(shadows),
                    AgxToneEngine.ShoulderPower(highlights));
                Assert.True(
                    current > previous,
                    $"Not strict at x={x:R}, c={contrast}, " +
                    $"h={highlights}, s={shadows}: {previous:R} -> {current:R}.");
                previous = current;
            }
        }
    }

    [Fact]
    public void Sigmoid_PreservesPivotAcrossSliderGrid()
    {
        foreach (var contrast in SliderGrid)
        foreach (var highlights in SliderGrid)
        foreach (var shadows in SliderGrid)
        {
            var actual = AgxToneEngine.EvaluateSigmoid(
                AgxToneEngine.XPivot,
                AgxToneEngine.Slope(contrast),
                AgxToneEngine.ToePower(shadows),
                AgxToneEngine.ShoulderPower(highlights));
            Assert.InRange(
                Math.Abs(actual - AgxToneEngine.YPivot),
                0,
                1e-12);
        }
    }

    [Fact]
    public void FullCrossing_PreservesPostGainMiddleGreyAcrossSlidersAndSourceEv()
    {
        double[] sourceExposureValues = [-2.0, -0.75, 0, 0.625, 2.0];
        var expected = ToneLut.SrgbEncode(AgxToneEngine.MiddleGrey);

        foreach (var sourceExposure in sourceExposureValues)
        foreach (var contrast in SliderGrid)
        foreach (var highlights in SliderGrid)
        foreach (var shadows in SliderGrid)
        {
            var baseGrey = AgxToneEngine.MiddleGrey * Math.Pow(2, -sourceExposure);
            var result = AgxCrossing.TransformAnalytic(
                new AgxRgb(baseGrey, baseGrey, baseGrey),
                Parameters(
                    contrast,
                    highlights,
                    shadows,
                    sourceExposureEv: sourceExposure));

            AssertClose(result.Red, expected, 2e-14);
            AssertClose(result.Green, expected, 2e-14);
            AssertClose(result.Blue, expected, 2e-14);
        }
    }

    [Fact]
    public void FullCrossing_PreservesAchromaticInputsAndBoundsOutput()
    {
        double[] achromatic = [0, 1e-8, 0.001, 0.18, 0.5, 1];
        foreach (var contrast in SliderGrid)
        foreach (var highlights in SliderGrid)
        foreach (var shadows in SliderGrid)
        foreach (var input in achromatic)
        {
            var result = AgxCrossing.TransformAnalytic(
                new AgxRgb(input, input, input),
                Parameters(contrast, highlights, shadows));

            AssertClose(result.Red, result.Green, 2e-14);
            AssertClose(result.Red, result.Blue, 2e-14);
            Assert.InRange(result.Red, 0, 1);
            Assert.InRange(result.Green, 0, 1);
            Assert.InRange(result.Blue, 0, 1);
        }

        AgxRgb[] chromatic =
        [
            new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
            new(1, 1, 0), new(0, 1, 1), new(1, 0, 1),
            new(0.02, 0.18, 0.9)
        ];
        foreach (var input in chromatic)
        {
            var result = AgxCrossing.TransformAnalytic(input, Parameters());
            Assert.InRange(result.Red, 0, 1);
            Assert.InRange(result.Green, 0, 1);
            Assert.InRange(result.Blue, 0, 1);
        }
    }

    [Fact]
    public void PositiveHighlightsAndShadows_BrightenTheirSidesOfPivot()
    {
        foreach (var x in new[]
                 {
                     AgxToneEngine.XPivot + 0.05,
                     AgxToneEngine.XPivot + 0.2,
                     0.95
                 })
        {
            var previous = double.NegativeInfinity;
            foreach (var highlights in SliderGrid)
            {
                var current = AgxToneEngine.EvaluateSigmoid(
                    x,
                    AgxToneEngine.NeutralSlope,
                    AgxToneEngine.NeutralToePower,
                    AgxToneEngine.ShoulderPower(highlights));
                Assert.True(current > previous);
                previous = current;
            }
        }

        foreach (var x in new[]
                 {
                     0.05,
                     AgxToneEngine.XPivot - 0.2,
                     AgxToneEngine.XPivot - 0.05
                 })
        {
            var previous = double.NegativeInfinity;
            foreach (var shadows in SliderGrid)
            {
                var current = AgxToneEngine.EvaluateSigmoid(
                    x,
                    AgxToneEngine.NeutralSlope,
                    AgxToneEngine.ToePower(shadows),
                    AgxToneEngine.NeutralShoulderPower);
                Assert.True(current > previous);
                previous = current;
            }
        }
    }

    [Fact]
    public void FoldRefund_RestoresGreyExactlyOnce()
    {
        var crossing = new AgxCrossing(
            Parameters(),
            new[,]
            {
                { 2.0, 0, 0 },
                { 0, 2.0, 0 },
                { 0, 0, 2.0 }
            });

        var result = crossing.TransformAnalytic(new AgxRgb(0.09, 0.09, 0.09));
        var expected = ToneLut.SrgbEncode(AgxToneEngine.MiddleGrey);

        Assert.Equal(2, crossing.Fold, 14);
        AssertClose(result.Red, expected, 2e-14);
        AssertClose(result.Green, expected, 2e-14);
        AssertClose(result.Blue, expected, 2e-14);
    }

    internal static AgxToneParameters Parameters(
        int contrast = 0,
        int highlights = 0,
        int shadows = 0,
        double exposureEv = 0,
        double sourceExposureEv = 0) =>
        new(
            exposureEv,
            sourceExposureEv,
            contrast,
            highlights,
            shadows,
            new CurveData());

    private static void AssertClose(double actual, double expected, double tolerance) =>
        Assert.True(
            Math.Abs(actual - expected) <= tolerance,
            $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
}

using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AgxToneEngineDerivationTests
{
    [Fact]
    public void PinnedScalarConstants_MatchTheirConstructions()
    {
        Assert.Equal(16.5, AgxToneEngine.EvWindow, 15);
        Assert.Equal(10.0 / 16.5, AgxToneEngine.XPivot, 15);
        Assert.Equal(
            AgxToneEngine.MiddleGrey,
            Math.Pow(AgxToneEngine.YPivot, AgxToneEngine.DisplayGamma),
            14);
        Assert.Equal(2.0, AgxToneEngine.Slope(0), 15);
        Assert.Equal(3.0, AgxToneEngine.ToePower(0), 15);
        Assert.Equal(3.25, AgxToneEngine.ShoulderPower(0), 15);
        Assert.Equal(Math.Sqrt(2) * 2, AgxToneEngine.Slope(100), 14);
        Assert.Equal(Math.Sqrt(2), AgxToneEngine.Slope(-100), 14);
        Assert.Equal(1.5, AgxToneEngine.ToePower(100), 14);
        Assert.Equal(6.0, AgxToneEngine.ToePower(-100), 14);
        Assert.Equal(6.5, AgxToneEngine.ShoulderPower(100), 14);
        Assert.Equal(1.625, AgxToneEngine.ShoulderPower(-100), 14);
    }

    [Fact]
    public void Inset_ReDerivesFromRotatedRec2020PrimariesAndCompression()
    {
        double[][] primaries =
        [
            [0.708, 0.292],
            [0.170, 0.797],
            [0.131, 0.046]
        ];
        double[] white = [0.3127, 0.3290];
        double[] rotationDegrees = [4.75, -4.25, 4.5];
        var enlarged = primaries
            .Select((primary, index) =>
            {
                var radians = rotationDegrees[index] * Math.PI / 180;
                var deltaX = primary[0] - white[0];
                var deltaY = primary[1] - white[1];
                var rotatedX = Math.Cos(radians) * deltaX -
                    Math.Sin(radians) * deltaY;
                var rotatedY = Math.Sin(radians) * deltaX +
                    Math.Cos(radians) * deltaY;
                return new[]
                {
                    white[0] + rotatedX / (1 - 0.20),
                    white[1] + rotatedY / (1 - 0.20)
                };
            })
            .ToArray();
        var baseToXyz = ColorScienceMatrixAssertions.DeriveRgbToXyz(
            primaries,
            white);
        var enlargedToXyz = ColorScienceMatrixAssertions.DeriveRgbToXyz(
            enlarged,
            white);
        var derived = Multiply(
            PrecisionColorCases.Invert(enlargedToXyz),
            baseToXyz);

        ColorScienceMatrixAssertions.AssertMatrixClose(
            AgxToneEngine.InsetMatrix,
            derived,
            1e-12,
            "pinned inset vs primary derivation");
    }

    [Fact]
    public void Outset_IsTheExactInverseAndBothMatricesPreserveAchromaticValues()
    {
        var inverse = PrecisionColorCases.Invert(AgxToneEngine.InsetMatrix);
        ColorScienceMatrixAssertions.AssertMatrixClose(
            AgxToneEngine.OutsetMatrix,
            inverse,
            1e-12,
            "pinned outset vs exact inverse");

        foreach (var matrix in new[]
                 {
                     AgxToneEngine.InsetMatrix,
                     AgxToneEngine.OutsetMatrix
                 })
        for (var row = 0; row < 3; row++)
        {
            var sum = matrix[row, 0] + matrix[row, 1] + matrix[row, 2];
            Assert.InRange(Math.Abs(sum - 1), 0, 5e-16);
        }
    }

    [Fact]
    public void Sigmoid_MatchesIndependentNormativeEquation()
    {
        int[] values = [-100, -50, 0, 50, 100];
        foreach (var contrast in values)
        foreach (var highlights in values)
        foreach (var shadows in values)
        for (var sample = 0; sample <= 1000; sample++)
        {
            var x = sample / 1000.0;
            var slope = 2 * Math.Pow(2, contrast / 200.0);
            var toe = 3 * Math.Pow(2, -shadows / 100.0);
            var shoulder = 3.25 * Math.Pow(2, highlights / 100.0);
            var expected = ReferenceSigmoid(x, slope, toe, shoulder);
            var actual = AgxToneEngine.EvaluateSigmoid(x, slope, toe, shoulder);
            Assert.InRange(Math.Abs(actual - expected), 0, 2e-15);
        }
    }

    [Fact]
    public void LutNodes_MatchAnalyticChainExactly()
    {
        var parameters = AgxToneEnginePropertyTests.Parameters(
            contrast: 50,
            highlights: -50,
            shadows: 100,
            exposureEv: 1.25,
            sourceExposureEv: -0.375);
        var lut = AgxToneLut.Compose(parameters, fold: 1.37);

        Assert.Equal(AgxToneLut.Length, lut.Length);
        for (var index = 0; index < lut.Length; index++)
        {
            Assert.Equal(
                AgxToneEngine.EvaluateTone(
                    index / (double)(AgxToneLut.Length - 1),
                    parameters,
                    fold: 1.37),
                lut[index]);
        }
    }

    [Fact]
    public void LutInterpolation_StaysWithinOneFinalQ16LsbAtSliderExtremes()
    {
        var tolerance = 1.0 / ushort.MaxValue;
        foreach (var contrast in new[] { -100, 100 })
        foreach (var highlights in new[] { -100, 100 })
        foreach (var shadows in new[] { -100, 100 })
        {
            var parameters = AgxToneEnginePropertyTests.Parameters(
                contrast,
                highlights,
                shadows);
            var lut = AgxToneLut.Compose(parameters, fold: 1);
            for (var index = 0; index < AgxToneLut.Length - 1; index++)
            foreach (var fraction in new[] { 0.25, 0.5, 0.75 })
            {
                var input = (index + fraction) / (AgxToneLut.Length - 1);
                var analytic = AgxToneEngine.EvaluateTone(input, parameters, fold: 1);
                var interpolated = AgxToneLut.Interpolate(lut, input);
                var analyticCode = ToneLut.SrgbEncode(analytic);
                var interpolatedCode = ToneLut.SrgbEncode(interpolated);
                Assert.True(
                    Math.Abs(analyticCode - interpolatedCode) <= tolerance,
                    $"c={contrast}, h={highlights}, s={shadows}, " +
                    $"x={input:R}: analytic {analyticCode:R}, " +
                    $"interpolated {interpolatedCode:R}.");
            }
        }
    }

    [Fact]
    public void FusedPass_QuantizesOnlyTheFinalEncodedResult()
    {
        var crossing = new AgxCrossing(
            AgxToneEnginePropertyTests.Parameters(
                contrast: 50,
                highlights: 100,
                shadows: -50));
        ushort[] samples =
        [
            1, 1234, 65534,
            8192, 32768, 49152,
            65535, 40000, 500
        ];
        var expected = new ushort[samples.Length];
        for (var offset = 0; offset < samples.Length; offset += 3)
        {
            var transformed = crossing.TransformInterpolated(new AgxRgb(
                samples[offset] / (double)ushort.MaxValue,
                samples[offset + 1] / (double)ushort.MaxValue,
                samples[offset + 2] / (double)ushort.MaxValue));
            expected[offset] = Quantize(transformed.Red);
            expected[offset + 1] = Quantize(transformed.Green);
            expected[offset + 2] = Quantize(transformed.Blue);
        }

        crossing.Apply(samples);

        Assert.Equal(expected, samples);
    }

    private static double ReferenceSigmoid(
        double x,
        double slope,
        double toePower,
        double shoulderPower)
    {
        var xp = 10.0 / 16.5;
        var yp = Math.Pow(0.18, 1.0 / 2.2);
        if (x <= xp)
        {
            var scale = ReferenceScale(xp, yp, slope, toePower);
            var z = slope * (xp - x) / scale;
            return yp - scale * z /
                Math.Pow(1 + Math.Pow(z, toePower), 1 / toePower);
        }

        var shoulderScale = ReferenceScale(
            1 - xp,
            1 - yp,
            slope,
            shoulderPower);
        var shoulderZ = slope * (x - xp) / shoulderScale;
        return yp + shoulderScale * shoulderZ /
            Math.Pow(
                1 + Math.Pow(shoulderZ, shoulderPower),
                1 / shoulderPower);
    }

    private static double ReferenceScale(
        double limitX,
        double limitY,
        double slope,
        double power) =>
        Math.Pow(
            Math.Pow(slope * limitX, -power) *
            (Math.Pow(slope * limitX / limitY, power) - 1),
            -1 / power);

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

    private static ushort Quantize(double value) =>
        (ushort)Math.Round(
            Math.Clamp(value, 0, 1) * ushort.MaxValue,
            MidpointRounding.AwayFromZero);
}

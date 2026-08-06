using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WhiteBalanceModelTests
{
    [Theory]
    [InlineData(2850, 0)]
    [InlineData(4250, -40)]
    [InlineData(6504, 0)]
    [InlineData(10000, 75)]
    public void MatchingTargetAndAnchor_IsExactIdentity(
        double kelvin,
        double tint)
    {
        var matrix = WhiteBalanceModel.CreateMatrix(
            kelvin,
            tint,
            kelvin,
            tint);

        AssertMatrixClose(ChromaticAdaptation.Identity(), matrix, 0);
        var normalized = ChromaticAdaptation.NormalizeForRender(matrix);
        AssertMatrixClose(ChromaticAdaptation.Identity(), normalized.Matrix, 0);
        Assert.Equal(1, normalized.Fold);
    }

    [Fact]
    public void HigherTargetKelvin_WarmsNeutral()
    {
        var matrix = WhiteBalanceModel.CreateMatrix(6500, 0, 3000, 0);
        var neutral = ChromaticAdaptation.Multiply(matrix, [1, 1, 1]);

        Assert.True(neutral[0] > neutral[2]);
    }

    [Fact]
    public void PositiveTint_SuppressesGreen()
    {
        var baseline = ChromaticAdaptation.Multiply(
            WhiteBalanceModel.CreateMatrix(5500, 0, 5500, 0),
            [1, 1, 1]);
        var magenta = ChromaticAdaptation.Multiply(
            WhiteBalanceModel.CreateMatrix(5500, 50, 5500, 0),
            [1, 1, 1]);

        Assert.True(
            magenta[1] / magenta.Sum() <
            baseline[1] / baseline.Sum(),
            $"baseline={string.Join(",", baseline)}, magenta={string.Join(",", magenta)}");
        Assert.True(magenta[1] < magenta[0]);
        Assert.True(magenta[1] < magenta[2]);
    }

    [Fact]
    public void D65WhitePoint_MatchesReference()
    {
        var xyz = WhiteBalanceModel.GetWhitePointXyz(6504, 0);

        Assert.InRange(xyz[0], 0.9504 - 0.002, 0.9504 + 0.002);
        Assert.Equal(1, xyz[1]);
        Assert.InRange(xyz[2], 1.0888 - 0.002, 1.0888 + 0.002);
    }

    [Fact]
    public void IlluminantAWhitePoint_MatchesReference()
    {
        var uv = WhiteBalanceModel.GetWhitePointUv(2856, 0);

        Assert.InRange(uv.U, 0.2559 - 0.001, 0.2559 + 0.001);
        Assert.InRange(uv.V, 0.3496 - 0.001, 0.3496 + 0.001);
    }

    [Fact]
    public void Locus_IsContinuousAndStrictlyDecreasingInU()
    {
        var previous = WhiteBalanceModel.GetWhitePointUv(2000, 0);
        for (var kelvin = 2025; kelvin <= 12000; kelvin += 25)
        {
            var current = WhiteBalanceModel.GetWhitePointUv(kelvin, 0);
            var distance = Math.Sqrt(
                Math.Pow(current.U - previous.U, 2) +
                Math.Pow(current.V - previous.V, 2));

            Assert.True(
                distance < 2.1e-3,
                $"Locus jump {distance:E4} at {kelvin - 25}→{kelvin} K.");
            Assert.True(
                current.U < previous.U,
                $"Locus u was not decreasing at {kelvin} K.");
            previous = current;
        }
    }

    [Fact]
    public void KelvinTintUv_RoundTripsAcrossSliderGrid()
    {
        double[] kelvins = [2500, 3500, 4000, 4250, 4500, 5500, 6504, 8000, 10000];
        double[] tints = [-50, -25, 0, 25, 50];
        foreach (var kelvin in kelvins)
        {
            foreach (var tint in tints)
            {
                var uv = WhiteBalanceModel.GetWhitePointUv(kelvin, tint);
                var estimated = WhiteBalanceModel.EstimateKelvinTintFromUv(
                    uv.U,
                    uv.V);

                Assert.InRange(estimated.kelvin, kelvin - 50, kelvin + 50);
                Assert.InRange(estimated.tint, tint - 2, tint + 2);
            }
        }
    }

    [Fact]
    public void RenderNormalization_BoundsWhiteForMatrixGrid()
    {
        double[] anchors = [2850, 5500, 9000];
        double[] targets = [2000, 2850, 4000, 4250, 6500, 10000, 12000];
        double[] tints = [-100, -50, 0, 50, 100];
        foreach (var anchor in anchors)
        {
            foreach (var target in targets)
            {
                foreach (var tint in tints)
                {
                    var matrix = WhiteBalanceModel.CreateMatrix(
                        target,
                        tint,
                        anchor,
                        0);
                    var normalized = ChromaticAdaptation.NormalizeForRender(matrix);
                    var white = ChromaticAdaptation.Multiply(
                        normalized.Matrix,
                        [1, 1, 1]);

                    Assert.True(normalized.Fold >= 1);
                    foreach (var component in white)
                    {
                        Assert.True(
                            component <= 1 + 1e-9,
                            $"{target} K/{tint} tint exceeded one: {component}.");
                    }
                }
            }
        }
    }

    [Fact]
    public void GainMatrix_IsDiagonal()
    {
        var matrix = WhiteBalanceModel.CreateGainMatrix([1.4, 1, 0.8]);

        AssertMatrixClose(
            new[,] { { 1.4, 0, 0 }, { 0, 1.0, 0 }, { 0, 0, 0.8 } },
            matrix,
            0);
    }

    [Fact]
    public void MissingRawAnchors_UseDocumentedFallback()
    {
        Assert.Equal(
            (5500d, 0d),
            WhiteBalanceModel.EstimateAsShot(null, null));
        Assert.Equal(
            (5500d, 0d),
            WhiteBalanceModel.EstimateAsShot([2, 1, 1.5], null));
    }

    [Fact]
    public void SyntheticCameraNeutral_RecoversAnchor()
    {
        var expectedKelvin = 5500d;
        var xyz = WhiteBalanceModel.GetWhitePointXyz(expectedKelvin, 0);
        var srgbWhite = ChromaticAdaptation.XyzToLinearSrgb(xyz);
        var camMul = srgbWhite.Select(value => 1 / value).ToArray();

        var estimated = WhiteBalanceModel.EstimateAsShot(
            camMul,
            ChromaticAdaptation.Identity());

        Assert.InRange(
            estimated.kelvin,
            expectedKelvin - 50,
            expectedKelvin + 50);
        Assert.InRange(estimated.tint, -2, 2);
    }

    [Fact]
    public void FourChannelCameraNeutral_UsesEveryMatrixColumn()
    {
        var expectedKelvin = 5500d;
        var xyz = WhiteBalanceModel.GetWhitePointXyz(expectedKelvin, 0);
        var srgbWhite = ChromaticAdaptation.XyzToLinearSrgb(xyz);
        double[] cameraNeutral =
        [
            srgbWhite[0],
            srgbWhite[1] * 0.2,
            srgbWhite[2],
            srgbWhite[1] * 0.8
        ];
        var camMul = cameraNeutral.Select(value => 1 / value).ToArray();
        double[,] camToSrgb =
        {
            { 1, 0, 0, 0 },
            { 0, 1, 0, 1 },
            { 0, 0, 1, 0 }
        };

        var estimated = WhiteBalanceModel.EstimateAsShot(
            camMul,
            camToSrgb);

        Assert.InRange(
            estimated.kelvin,
            expectedKelvin - 50,
            expectedKelvin + 50);
        Assert.InRange(estimated.tint, -2, 2);
    }

    [Fact]
    public void MismatchedCameraFactShapes_UseDocumentedFallback()
    {
        var estimated = WhiteBalanceModel.EstimateAsShot(
            [2, 1, 1.5, 1],
            ChromaticAdaptation.Identity());

        Assert.Equal((5500d, 0d), estimated);
    }

    [Fact]
    public void RenderNormalization_RejectsNonFiniteMatrix()
    {
        var matrix = ChromaticAdaptation.Identity();
        matrix[1, 1] = double.NaN;

        Assert.Throws<ArgumentException>(
            () => ChromaticAdaptation.NormalizeForRender(matrix));
    }

    [Fact]
    public void UvEstimation_RejectsNonFiniteCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WhiteBalanceModel.EstimateKelvinTintFromUv(
                double.NaN,
                0.3));
    }

    [Fact]
    public void IdentityMatrix_StillRejectsNonFiniteModelInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => WhiteBalanceModel.CreateMatrix(
                double.PositiveInfinity,
                0,
                double.PositiveInfinity,
                0));
    }

    [Fact]
    public void DiagonalMatrix_RejectsNullInput()
    {
        Assert.Throws<ArgumentNullException>(
            () => ChromaticAdaptation.CreateDiagonal(null!));
    }

    private static void AssertMatrixClose(
        double[,] expected,
        double[,] actual,
        double tolerance)
    {
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                Assert.InRange(
                    actual[row, column],
                    expected[row, column] - tolerance,
                    expected[row, column] + tolerance);
            }
        }
    }
}

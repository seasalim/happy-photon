using HappyPhoton.Services;
using HappyPhoton.Models;
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
    public void PickedGainDisplay_UsesRec2020WorkingBasis()
    {
        double[] gains = [1.8, 1, 0.7];
        var actual = WhiteBalanceModel.EstimateFromGains(gains);
        var workingWhite = new[] { 1 / gains[0], 1 / gains[1], 1 / gains[2] };
        var sum = workingWhite.Sum();
        workingWhite = workingWhite.Select(value => value / sum).ToArray();
        var xyz = ChromaticAdaptation.LinearRec2020ToXyz(workingWhite);
        var denominator = xyz[0] + 15 * xyz[1] + 3 * xyz[2];
        var expected = WhiteBalanceModel.EstimateKelvinTintFromUv(
            4 * xyz[0] / denominator,
            6 * xyz[1] / denominator);
        var oldXyz = ChromaticAdaptation.LinearSrgbToXyz(workingWhite);
        var oldDenominator = oldXyz[0] + 15 * oldXyz[1] + 3 * oldXyz[2];
        var oldBasis = WhiteBalanceModel.EstimateKelvinTintFromUv(
            4 * oldXyz[0] / oldDenominator,
            6 * oldXyz[1] / oldDenominator);

        Assert.Equal(expected.kelvin, actual.kelvin, 10);
        Assert.Equal(expected.tint, actual.tint, 10);
        Assert.NotEqual(oldBasis, actual);
    }

    [Fact]
    public void AsShotWhiteBalanceFactor_RemainsExactIdentity()
    {
        var settings = new EditSettings();
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            [1000, 1000, 1000],
            isRaw: true);
        var info = baseImage.Info;
        var whiteBalance = WhiteBalanceModel.CreateMatrix(
            info.AsShotKelvin,
            info.AsShotTint,
            info.AsShotKelvin,
            info.AsShotTint);

        AssertMatrixClose(ChromaticAdaptation.Identity(), whiteBalance, 0);
        var combined = RenderChromaticStage.CreateNormalizedMatrix(info, settings);
        Assert.Equal(1.6604910021, combined.Fold, 9);
    }

    [Fact]
    public void RawAsShot_UsesDocumentedFallbackWhenFactsAreMissing()
    {
        Assert.Equal(
            (5500d, 0d),
            WhiteBalanceModel.EstimateAsShot(null, null, null));
        Assert.Equal(
            (5500d, 0d),
            WhiteBalanceModel.EstimateAsShot(
                [2, 1, 1.5],
                ChromaticAdaptation.Identity(),
                null));
    }

    [Fact]
    public void CanonCameraFacts_RecordAbiV2Reference()
    {
        // Reference data for the ABI-v2 fix, not a check of production code:
        // real Canon facts showing rgb_cam's rows sum to 1 (so it consumes
        // daylight-balanced input) and cam_mul is not uniform (so 1 / cam_mul
        // could never have been the right input). RawBaseLoaderTests asserts
        // the same convention against actually decoded fixtures.
        double[] camMul = [2170, 1018, 1755];
        double[,] camToSrgb =
        {
            { 1.7229120731, -0.8995228410, 0.1766107678 },
            { 0.0193507429, 1.3393489122, -0.3586996551 },
            { 0.0241905022, -0.3149886429, 1.2907981407 }
        };

        Assert.NotEqual(camMul[0] / camMul[1], camMul[2] / camMul[1]);
        for (var row = 0; row < 3; row++)
        {
            var sum = Enumerable.Range(0, 3)
                .Sum(column => camToSrgb[row, column]);
            Assert.InRange(sum, 1 - 1e-9, 1 + 1e-9);
        }
    }

    [Fact]
    public void SyntheticPreMultiplierProjection_RecoversAnchor()
    {
        var expectedKelvin = 6504d;
        var xyz = WhiteBalanceModel.GetWhitePointXyz(expectedKelvin, 0);
        var cameraNeutral = ChromaticAdaptation.XyzToLinearSrgb(xyz);
        double[] camMul = [2.5, 1.25, 4];
        var preMul = cameraNeutral
            .Select((value, channel) => value * camMul[channel])
            .ToArray();

        var estimated = WhiteBalanceModel.EstimateAsShot(
            camMul,
            ChromaticAdaptation.Identity(),
            preMul);

        Assert.InRange(estimated.kelvin, expectedKelvin - 50, expectedKelvin + 50);
        Assert.InRange(estimated.tint, -2, 2);
    }

    [Fact]
    public void FourChannelPreMultiplierProjection_UsesEveryMatrixColumn()
    {
        var expectedKelvin = 6504d;
        var xyz = WhiteBalanceModel.GetWhitePointXyz(expectedKelvin, 0);
        var srgbWhite = ChromaticAdaptation.XyzToLinearSrgb(xyz);
        double[] cameraNeutral =
        [
            srgbWhite[0],
            srgbWhite[1] * 0.2,
            srgbWhite[2],
            srgbWhite[1] * 0.8
        ];
        double[] camMul = [2, 3, 4, 5];
        var preMul = cameraNeutral
            .Select((value, channel) => value * camMul[channel])
            .ToArray();
        double[,] camToSrgb =
        {
            { 1, 0, 0, 0 },
            { 0, 1, 0, 1 },
            { 0, 0, 1, 0 }
        };

        var estimated = WhiteBalanceModel.EstimateAsShot(
            camMul,
            camToSrgb,
            preMul);

        Assert.InRange(estimated.kelvin, expectedKelvin - 50, expectedKelvin + 50);
        Assert.InRange(estimated.tint, -2, 2);
    }

    [Fact]
    public void MismatchedCameraFactShapes_UseDocumentedFallback()
    {
        var estimated = WhiteBalanceModel.EstimateAsShot(
            [2, 1, 1.5, 1],
            ChromaticAdaptation.Identity(),
            [1, 1, 1, 1]);

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

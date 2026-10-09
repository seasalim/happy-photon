using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class DcpMatrixAndHueSatTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AsShotWhiteXy_DualMatricesMatchExplicitNeutralAndMapToD50(bool signaturesMatch)
    {
        var profile = CreateProfile(
            color2: new[,] { { 0.8, 0.1, 0.0 }, { 0.1, 1.1, 0.1 }, { 0.0, 0.1, 0.7 } },
            illuminant1: 17, illuminant2: 22, signature: "profile");
        var camera = new DcpCameraData([1.1, 0.9, 1.2], SkewCalibration(),
            SkewCalibration(), null, null, null, signaturesMatch ? "profile" : "camera")
        {
            AsShotWhiteXy = [0.3127, 0.3290]
        };
        // Independent D65 McCamy result and reciprocal-CCT interpolation between 2850 and 7500 K.
        const double kelvin = 6505.080591307478;
        var weight = (1 / kelvin - 1 / 2850.0) / (1 / 7500.0 - 1 / 2850.0);
        var cm = new double[3, 3];

        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            cm[row, column] = profile.ColorMatrix1[row, column] * (1 - weight) +
                profile.ColorMatrix2![row, column] * weight;
        }

        var xyz = new[] { 0.3127 / 0.3290, 1, (1 - 0.3127 - 0.3290) / 0.3290 };
        var expected = ChromaticAdaptation.Multiply(cm, xyz);
        expected = ChromaticAdaptation.Multiply(
            signaturesMatch ? SkewCalibration() : ChromaticAdaptation.Identity(), expected);
        expected = expected.Select((value, index) => value * camera.AnalogBalance![index]).ToArray();
        expected = expected.Select(value => value / expected[1]).ToArray();
        var neutral = DcpMatrixCalculator.DeriveAsShotNeutral(profile, camera)!;

        for (var channel = 0; channel < 3; channel++)
        {
            Assert.InRange(Math.Abs(expected[channel] - neutral[channel]), 0, 1e-6);
        }

        var actual = DcpMatrixCalculator.Create(Resolution(profile), camera, RawCameraFactSnapshot.Empty, 5500);
        var explicitNeutral = DcpMatrixCalculator.Create(Resolution(profile),
            camera with { AsShotNeutral = expected, AsShotWhiteXy = null },
            Facts(expected.Select(value => 1 / value).ToArray()), kelvin);
        Assert.True(actual.IsActive);

        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            Assert.Equal(explicitNeutral.CameraToRec2020![row, column], actual.CameraToRec2020![row, column], 6);
        }

        var workingWhite = ChromaticAdaptation.Multiply(actual.CameraToRec2020!, [1, 1, 1]);
        var xyzD65 = ChromaticAdaptation.LinearRec2020ToXyz(workingWhite);
        var xyzD50 = ChromaticAdaptation.Multiply(
            ChromaticAdaptation.CreateBradfordMatrix([0.95047, 1, 1.08883], [0.96422, 1, 0.82521]), xyzD65);
        var y = xyzD50[1];
        var fx = Math.Cbrt(xyzD50[0] / y / 0.96422);
        var fz = Math.Cbrt(xyzD50[2] / y / 0.82521);
        var lab = new PrecisionLab(100, 500 * (fx - 1), 200 * (1 - fz));
        Assert.InRange(PrecisionDeltaE.Ciede2000(lab, new PrecisionLab(100, 0, 0)), 0, 0.5);
    }

    [Theory]
    [InlineData(0, 0.3)]
    [InlineData(0.3, 0)]
    [InlineData(0.7, 0.4)]
    [InlineData(0.3, 0.1858)]
    [InlineData(double.NaN, 0.3)]
    public void AsShotWhiteXy_InvalidWhitePointRejectsProfile(double x, double y)
    {
        var result = DcpMatrixCalculator.Create(Resolution(CreateProfile()),
            DcpCameraData.Defaults with { AsShotWhiteXy = [x, y] }, RawCameraFactSnapshot.Empty, 5500);

        Assert.False(result.IsActive);
        Assert.Equal(DcpProfileErrorCode.UnsupportedVariant, result.Status);
    }

    [Fact]
    public void AsShotWhiteXy_NeutralWinsEvenWhenXyIsInvalid()
    {
        var camera = DcpCameraData.Defaults with { AsShotNeutral = [1, 1, 1], AsShotWhiteXy = [0, 0] };
        var profile = CreateProfile();
        var actual = DcpMatrixCalculator.Create(Resolution(profile), camera, Facts([1, 1, 1]), 6500);
        var expected = DcpMatrixCalculator.Create(Resolution(profile),
            camera with { AsShotWhiteXy = null }, Facts([1, 1, 1]), 6500);

        Assert.Null(DcpMatrixCalculator.DeriveAsShotNeutral(profile, camera));
        Assert.Equal(expected.CameraToRec2020, actual.CameraToRec2020);
    }
}

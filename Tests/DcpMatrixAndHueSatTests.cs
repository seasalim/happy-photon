using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DcpMatrixAndHueSatTests
{
    [Fact]
    public void BalancedSeam_KnownCalibrationRoundTripsNeutralToD50White()
    {
        var profile = CreateProfile(
            forward1: ToMatrix(DcpProfileReaderTests.D50Forward(1)),
            signature: "round-trip");
        var analogBalance = new[] { 2.0, 0.5, 1.5 };
        var cameraCalibration = new[,]
        {
            { 1.1, 0.0, 0.0 },
            { 0.0, 0.9, 0.0 },
            { 0.0, 0.0, 1.2 }
        };
        var referenceNeutral = new[] { 0.5, 1.0, 0.25 };
        var neutral = ChromaticAdaptation.Multiply(
            ChromaticAdaptation.Multiply(
                ChromaticAdaptation.CreateDiagonal(analogBalance),
                cameraCalibration),
            referenceNeutral);
        var camMul = neutral.Select(value => 1 / value).ToArray();
        var facts = Facts(camMul);
        var camera = new DcpCameraData(
            analogBalance,
            cameraCalibration,
            null,
            null,
            null,
            neutral,
            "round-trip");

        var result = DcpMatrixCalculator.Create(
            Resolution(profile),
            camera,
            facts,
            6500);

        Assert.True(result.IsActive);
        var balance = camMul.Select(value => value / camMul[1]).ToArray();
        var balancedNeutral = neutral
            .Select((value, index) => value * balance[index])
            .ToArray();
        var white = ChromaticAdaptation.Multiply(
            result.CameraToRec2020!,
            balancedNeutral);
        Assert.All(white, component => Assert.Equal(1, component, 6));
    }

    [Fact]
    public void BalancedSeam_SeededCalibrationNeutralsStayWhite()
    {
        var random = new Random(0x5d0c);
        for (var sample = 0; sample < 100; sample++)
        {
            var analog = Enumerable.Range(0, 3)
                .Select(_ => 0.5 + random.NextDouble() * 1.5)
                .ToArray();
            var calibration = new[,]
            {
                { 0.75 + random.NextDouble() * 0.5, 0, 0 },
                { 0, 0.75 + random.NextDouble() * 0.5, 0 },
                { 0, 0, 0.75 + random.NextDouble() * 0.5 }
            };
            var reference = Enumerable.Range(0, 3)
                .Select(_ => 0.4 + random.NextDouble() * 0.8)
                .ToArray();
            var neutral = ChromaticAdaptation.Multiply(
                ChromaticAdaptation.Multiply(
                    ChromaticAdaptation.CreateDiagonal(analog),
                    calibration),
                reference);
            var camMul = neutral.Select(value => 1 / value).ToArray();
            var result = DcpMatrixCalculator.Create(
                Resolution(CreateProfile(
                    forward1: ToMatrix(DcpProfileReaderTests.D50Forward(1)),
                    signature: "seeded")),
                new DcpCameraData(
                    analog,
                    calibration,
                    null,
                    null,
                    null,
                    neutral,
                    "seeded"),
                Facts(camMul),
                6500);
            var balanced = neutral.Select((value, index) =>
                value * camMul[index] / camMul[1]).ToArray();
            var white = ChromaticAdaptation.Multiply(
                result.CameraToRec2020!,
                balanced);
            Assert.All(white, value => Assert.Equal(1, value, 6));
        }
    }

    [Fact]
    public void DualIlluminantWeight_IsInverseCctAndAsShotAnchored()
    {
        var profile = CreateProfile(
            color2: ToMatrix(DcpProfileReaderTests.ScaleIdentity(2)),
            illuminant1: 17,
            illuminant2: 21);
        var reciprocalMidpoint = 1 /
            ((1 / 2850.0 + 1 / 6500.0) / 2);

        var weight = DcpMatrixCalculator.GetInterpolationWeight(
            profile,
            reciprocalMidpoint);

        Assert.Equal(0.5, weight, 12);
        Assert.Equal(0, DcpMatrixCalculator.GetInterpolationWeight(profile, 2000));
        Assert.Equal(1, DcpMatrixCalculator.GetInterpolationWeight(profile, 9000));
    }

    [Fact]
    public void MissingWhiteBalance_RejectsProfileForBuiltInFallback()
    {
        var result = DcpMatrixCalculator.Create(
            Resolution(CreateProfile()),
            DcpCameraData.Defaults,
            RawCameraFactSnapshot.Empty,
            6500);

        Assert.False(result.IsActive);
        Assert.Equal(DcpProfileErrorCode.MissingWhiteBalance, result.Status);
    }

    [Fact]
    public void CalibrationSignatureMismatch_RejectsProfile()
    {
        var result = DcpMatrixCalculator.Create(
            Resolution(CreateProfile(signature: "profile")),
            new DcpCameraData(
                null,
                ChromaticAdaptation.Identity(),
                null,
                null,
                null,
                null,
                "camera"),
            Facts([1, 1, 1]),
            6500);

        Assert.False(result.IsActive);
        Assert.Equal(DcpProfileErrorCode.SignatureMismatch, result.Status);
    }

    [Fact]
    public void MissingSecondCameraCalibration_UsesIdentityDefault()
    {
        var profile = CreateProfile(
            color2: ToMatrix(DcpProfileReaderTests.ScaleIdentity(1.1)),
            illuminant1: 17,
            illuminant2: 21,
            signature: "camera");
        var result = DcpMatrixCalculator.Create(
            Resolution(profile),
            new DcpCameraData(
                null,
                ToMatrix(DcpProfileReaderTests.ScaleIdentity(0.9)),
                null,
                null,
                null,
                null,
                "camera"),
            Facts([1, 1, 1]),
            6500);

        Assert.True(result.IsActive);
    }

    [Fact]
    public void HueSatLookup_IsTrilinearWithHueWraparound()
    {
        var table = CreateRampTable();
        var map = new DcpHueSatMap(2, 2, 2, false, table, null, 0);

        var middle = DcpHueSatRenderer.Lookup(map, 90, 0.5, 0.5);
        var wrapped = DcpHueSatRenderer.Lookup(map, 315, 0, 0);

        Assert.Equal(35, middle.HueShift, 10);
        Assert.Equal(1.35, middle.SaturationScale, 6);
        Assert.Equal(0.825, middle.ValueScale, 6);
        Assert.Equal(2.5, wrapped.HueShift, 10);
    }

    [Fact]
    public void HueSatLookup_DualTablesShareMatrixInterpolationWeight()
    {
        var first = DcpProfileReaderTests.CreateTable(2, 2, 1, 0, 1, 1);
        var second = DcpProfileReaderTests.CreateTable(2, 2, 1, 20, 1.4f, 1);
        var map = new DcpHueSatMap(2, 2, 1, false, first, second, 0.25);

        var delta = DcpHueSatRenderer.Lookup(map, 20, 1, 0.2);

        Assert.Equal(5, delta.HueShift, 10);
        Assert.Equal(1.1, delta.SaturationScale, 6);
        Assert.Equal(1, delta.ValueScale, 10);
    }

    [Fact]
    public void HueSatLookup_TwoAndAHalfDimensionalMapIgnoresValue()
    {
        var table = DcpProfileReaderTests.CreateTable(2, 2, 1, 5, 1.1f, 0.8f);
        var map = new DcpHueSatMap(2, 2, 1, true, table, null, 0);

        Assert.Equal(
            DcpHueSatRenderer.Lookup(map, 120, 0.7, 0),
            DcpHueSatRenderer.Lookup(map, 120, 0.7, 1));
    }

    [Fact]
    public void HueSatEncoding_EncodesAndDecodesOnlyValueCoordinate()
    {
        var table = DcpProfileReaderTests.CreateTable(2, 2, 2, 0, 1, 0.5f);
        var map = new DcpHueSatMap(2, 2, 2, true, table, null, 0);

        var result = DcpHueSatRenderer.ApplyToHsv(map, 120, 1, 0.25);

        var encoded = 1.055 * Math.Pow(0.25, 1 / 2.4) - 0.055;
        var scaled = encoded * 0.5;
        var expected = Math.Pow((scaled + 0.055) / 1.055, 2.4);
        Assert.Equal(120, result.Hue, 12);
        Assert.Equal(1, result.Saturation, 12);
        Assert.Equal(expected, result.Value, 12);
        Assert.NotEqual(0.125, result.Value, 6);
    }

    [Fact]
    public void ProPhotoCrossing_MatchesIndependentColourScienceOracle()
    {
        var oracle = ColorScienceOracleData.Load();
        var working = oracle.Space("linear-rec2020-d65");
        var proPhoto = oracle.Space("linear-romm-d50");
        var adaptation = ColorScienceMatrixAssertions.ToMatrix(
            oracle.Adaptation("bradford-d65-to-d50").Matrix);
        var input = new[] { 0.2, 0.4, 0.8 };
        var expected = PrecisionColorCases.Transform(
            ColorScienceMatrixAssertions.ToMatrix(proPhoto.MatrixXyzToRgb),
            PrecisionColorCases.Transform(
                adaptation,
                PrecisionColorCases.Transform(
                    ColorScienceMatrixAssertions.ToMatrix(working.MatrixRgbToXyz),
                    input)));

        var actual = DcpHueSatRenderer.ConvertWorkingToProPhoto(input);
        var recovered = DcpHueSatRenderer.ConvertProPhotoToWorking(actual);

        for (var index = 0; index < 3; index++)
        {
            Assert.InRange(Math.Abs(actual[index] - expected[index]), 0, 5e-4);
            Assert.InRange(Math.Abs(recovered[index] - input[index]), 0, 5e-4);
        }
    }

    [Fact]
    public void PreparedRgbLut_TracksExactHueSatSequenceForSeededColors()
    {
        var table = DcpProfileReaderTests.CreateTable(
            6, 4, 3, 7, 1.08f, 0.96f);
        var map = DcpHueSatRenderer.Prepare(
            new DcpHueSatMap(6, 4, 3, true, table, null, 0));
        var random = new Random(0x7a51);
        var maximumError = 0.0;
        for (var sample = 0; sample < 1000; sample++)
        {
            var input = Enumerable.Range(0, 3)
                .Select(_ => random.NextDouble())
                .ToArray();
            var exact = DcpHueSatRenderer.TransformWorkingRgb(
                map, input[0], input[1], input[2]);
            var actual = DcpHueSatRenderer.EvaluateLut(map, input);
            maximumError = Math.Max(maximumError,
                Math.Abs(actual[0] - Math.Clamp(exact.Red, 0, 1)));
            maximumError = Math.Max(maximumError,
                Math.Abs(actual[1] - Math.Clamp(exact.Green, 0, 1)));
            maximumError = Math.Max(maximumError,
                Math.Abs(actual[2] - Math.Clamp(exact.Blue, 0, 1)));
        }
        Assert.InRange(maximumError, 0, 0.008);
    }

    private static DcpProfile CreateProfile(
        double[,]? color2 = null,
        double[,]? forward1 = null,
        int illuminant1 = 21,
        int? illuminant2 = null,
        string signature = "") => new(
            "Synthetic",
            "Synthetic Camera",
            ChromaticAdaptation.Identity(),
            color2,
            forward1,
            null,
            illuminant1,
            illuminant2,
            signature,
            0,
            0,
            0,
            0,
            false,
            null,
            null,
            new string('a', 64));

    private static DcpProfileResolution Resolution(DcpProfile profile) =>
        DcpProfileResolution.Success(
            new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = "synthetic.dcp",
                ContentHash = profile.ContentHash
            },
            profile);

    private static RawCameraFactSnapshot Facts(double[] camMul) => new(
        camMul,
        ChromaticAdaptation.Identity(),
        [1, 1, 1],
        null,
        false);

    private static double[,] ToMatrix(IReadOnlyList<double> values)
    {
        var result = new double[3, 3];
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            result[row, column] = values[row * 3 + column];
        }
        return result;
    }

    private static float[] CreateRampTable()
    {
        var result = new float[2 * 2 * 2 * 3];
        for (var value = 0; value < 2; value++)
        for (var saturation = 0; saturation < 2; saturation++)
        for (var hue = 0; hue < 2; hue++)
        {
            var ramp = hue * 10 + saturation * 20 + value * 40;
            var index = ((value * 2 + hue) * 2 + saturation) * 3;
            result[index] = ramp;
            result[index + 1] = 1 + ramp / 100f;
            result[index + 2] = 1 - ramp / 200f;
        }
        return result;
    }
}

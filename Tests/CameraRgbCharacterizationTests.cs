using System.Runtime.InteropServices;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CameraRgbCharacterizationTests
{
    [Fact]
    public void UsableMatrix_MatchesIndependentOracleVectors()
    {
        var oracle = ColorScienceOracleData.Load()
            .CameraCharacterizations.Single(value => value.Id == "synthetic-camera-rgb");
        var characterization = CameraRgbCharacterization.Create(
            Snapshot(ToMatrix(oracle.CameraToSrgb)));

        Assert.Equal(
            CameraRgbCharacterizationOutcome.Usable,
            characterization.Outcome);
        ColorScienceMatrixAssertions.AssertMatrixClose(
            characterization.CameraToRec2020,
            ToMatrix(oracle.CameraToRec2020),
            2e-12,
            "camera RGB to Rec.2020 oracle");

        var samples = oracle.Samples
            .SelectMany(sample => sample.CameraRgb)
            .Select(value => (ushort)Math.Round(
                value * ushort.MaxValue,
                MidpointRounding.AwayFromZero))
            .ToArray();
        using var image = characterization.ImportRgb16(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            oracle.Samples.Count,
            1);
        var actual = image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ?? [];
        var expected = oracle.Samples
            .SelectMany(sample => sample.Rec2020)
            .Select(ToQ16)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IdentitySentinel_DerivesFromCameraFromXyz()
    {
        var cameraFromXyz = new double[,]
        {
            { 0.58, 0.21, 0.08 },
            { 0.14, 0.72, 0.12 },
            { 0.03, 0.11, 0.91 }
        };
        var snapshot = Snapshot(
            camToSrgb: null,
            cameraFromXyz,
            identity: true);

        var characterization = CameraRgbCharacterization.Create(snapshot);

        Assert.Equal(
            CameraRgbCharacterizationOutcome.Derived,
            characterization.Outcome);
        var cameraFromSrgb = Multiply(
            cameraFromXyz,
            RgbColorSpaceMatrices.LinearSrgbToXyzD65PublishedRounded);
        NormalizeRows(cameraFromSrgb);
        var expectedCameraToSrgb = PrecisionColorCases.Invert(cameraFromSrgb);
        var oracle = ColorScienceOracleData.Load();
        var expectedWorking = Multiply(
            Multiply(
                ToMatrix(oracle.Space("linear-rec2020-d65").MatrixXyzToRgb),
                ToMatrix(oracle.Space("linear-srgb-d65").MatrixRgbToXyz)),
            expectedCameraToSrgb);
        ColorScienceMatrixAssertions.AssertMatrixClose(
            characterization.CameraToRec2020,
            expectedWorking,
            2e-12,
            "derived camera RGB to Rec.2020 oracle");
    }

    [Fact]
    public void MissingTransform_IsTypedPassthroughAndPreservesCodes()
    {
        ushort[] expected = [1, 0x1234, 0xFEDC];
        var characterization = CameraRgbCharacterization.Create(
            RawCameraFactSnapshot.Empty);

        using var image = characterization.ImportRgb16(
            MemoryMarshal.AsBytes(expected.AsSpan()),
            1,
            1);
        var actual = image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB);

        Assert.Equal(
            CameraRgbCharacterizationOutcome.UncharacterizedPassthrough,
            characterization.Outcome);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FourChannelTransform_IsRejectedWithoutTruncation()
    {
        var snapshot = Snapshot(new double[,]
        {
            { 1, 0, 0, 0.1 },
            { 0, 1, 0, 0.1 },
            { 0, 0, 1, 0.1 }
        });

        var exception = Assert.Throws<NotSupportedException>(
            () => CameraRgbCharacterization.Create(snapshot));

        Assert.Contains("three", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CameraFactCopy_PreservesDerivedFactBehindIdentitySentinel()
    {
        var source = new LibRawCameraFacts(
            [2, 1, 1.5f],
            new float[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } },
            [1, 1, 1],
            new float[,] { { 0.5f, 0.2f, 0.1f }, { 0.1f, 0.7f, 0.1f }, { 0, 0.1f, 0.9f } },
            null);

        var snapshot = RawCameraFactSnapshot.Copy(source);

        Assert.True(snapshot.IsIdentityCameraTransform);
        Assert.Null(snapshot.CamToSrgb);
        Assert.NotNull(snapshot.CameraFromXyz);
        Assert.Equal(3, snapshot.CameraFromXyz!.GetLength(0));
    }

    private static RawCameraFactSnapshot Snapshot(
        double[,]? camToSrgb,
        double[,]? cameraFromXyz = null,
        bool identity = false) =>
        new(
            [2, 1, 1.5],
            camToSrgb,
            [1, 1, 1],
            cameraFromXyz,
            identity);

    private static ushort ToQ16(double value) =>
        (ushort)Math.Round(
            Math.Clamp(value, 0, 1) * ushort.MaxValue,
            MidpointRounding.AwayFromZero);

    private static double[,] ToMatrix(double[][] values) => new[,]
    {
        { values[0][0], values[0][1], values[0][2] },
        { values[1][0], values[1][1], values[1][2] },
        { values[2][0], values[2][1], values[2][2] }
    };

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

    private static void NormalizeRows(double[,] matrix)
    {
        for (var row = 0; row < 3; row++)
        {
            var sum = matrix[row, 0] + matrix[row, 1] + matrix[row, 2];
            for (var column = 0; column < 3; column++)
            {
                matrix[row, column] /= sum;
            }
        }
    }
}

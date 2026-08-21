using System.Runtime.InteropServices;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawWorkingSpaceTests
{
    private readonly ITestOutputHelper _output;

    public RawWorkingSpaceTests(ITestOutputHelper output) => _output = output;

    private static string CanonFixture => Path.Combine(
        GoldenTestPaths.AssetDirectory,
        "canon-eos-350d.cr2");

    [Theory]
    [InlineData("canon-eos-350d.cr2", LibRawHighlightMode.Clip, LibRawFbddMode.Off)]
    [InlineData("canon-eos-350d.cr2", LibRawHighlightMode.Blend, LibRawFbddMode.Off)]
    [InlineData("canon-eos-350d.cr2", LibRawHighlightMode.Clip, LibRawFbddMode.Full)]
    [InlineData("fujifilm-x30.raf", LibRawHighlightMode.Clip, LibRawFbddMode.Off)]
    [InlineData("fujifilm-x30.raf", LibRawHighlightMode.Blend, LibRawFbddMode.Off)]
    [InlineData("fujifilm-x30.raf", LibRawHighlightMode.Clip, LibRawFbddMode.Full)]
    public void BuiltInCharacterization_MatchesLibRawRec2020Comparator(
        string fileName,
        LibRawHighlightMode highlight,
        LibRawFbddMode fbdd)
    {
        var path = Path.Combine(GoldenTestPaths.AssetDirectory, fileName);
        using var expected = DecodeImage(
            path,
            LibRawOutputConfiguration.LinearRec2020(highlight, fbdd, halfSize: true));
        var actual = DecodeCharacterized(path, highlight, fbdd);
        using var actualImage = actual.Image;

        var comparison = GoldenImageComparer.Compare(
            expected,
            actualImage,
            GoldenComparisonDomain.LinearRec2020);
        _output.WriteLine(
            $"{fileName} {highlight}/{fbdd}: outcome={actual.Outcome}; " +
            $"mean ΔE76={comparison.MeanDeltaE:R}; p99={comparison.P99DeltaE:R}");

        Assert.Equal(CameraRgbCharacterizationOutcome.Usable, actual.Outcome);
        Assert.InRange(comparison.MeanDeltaE, 0, 1.1);
        Assert.InRange(comparison.P99DeltaE, 0, 9.5);
    }

    [Fact]
    public void CameraToSrgbFact_MatchesCameraFromXyzSemanticOracle()
    {
        using var context = LibRawContext.Open(CanonFixture);
        context.Unpack();
        var facts = context.GetCameraFacts() ??
            throw new InvalidOperationException("Camera facts were unavailable.");
        var cameraFromXyz = facts.CameraFromXyz ??
            throw new InvalidOperationException("camera_from_xyz was unavailable.");
        Assert.Equal(3, cameraFromXyz.GetLength(0));
        Assert.Equal(3, cameraFromXyz.GetLength(1));

        var cameraFromSrgb = Multiply(
            ToDouble(cameraFromXyz),
            RgbColorSpaceMatrices.LinearSrgbToXyzD65PublishedRounded);
        NormalizeRows(cameraFromSrgb);
        var expectedCameraToSrgb = PrecisionColorCases.Invert(cameraFromSrgb);
        ColorScienceMatrixAssertions.AssertMatrixClose(
            ToDouble(facts.CameraToSrgb),
            expectedCameraToSrgb,
            2e-4,
            "camera_to_srgb semantic oracle");

        var cameraFromRec2020 = Multiply(
            ToDouble(cameraFromXyz),
            RgbColorSpaceMatrices.LinearRec2020ToXyzD65DerivedExact);
        NormalizeRows(cameraFromRec2020);
        var rec2020Inverse = PrecisionColorCases.Invert(cameraFromRec2020);
        Assert.True(MaximumDifference(
            ToDouble(facts.CameraToSrgb),
            rec2020Inverse) > 0.05);
    }

    private static DecodedImage Decode(LibRawOutputConfiguration configuration)
        => Decode(CanonFixture, configuration);

    private static DecodedImage Decode(
        string path,
        LibRawOutputConfiguration configuration)
    {
        using var context = LibRawContext.Open(path);
        context.Unpack();
        context.ConfigureOutput(configuration);
        context.Process();
        using var image = context.MakeProcessedImage();
        return new DecodedImage(
            checked((int)image.Description.Width),
            checked((int)image.Description.Height),
            MemoryMarshal.Cast<byte, ushort>(image.AsSpan()).ToArray());
    }

    private static MagickImage DecodeImage(
        string path,
        LibRawOutputConfiguration configuration)
    {
        var decoded = Decode(path, configuration);
        return RawBaseLoader.ImportRgb16(
            MemoryMarshal.AsBytes(decoded.Samples.AsSpan()),
            decoded.Width,
            decoded.Height);
    }

    private static CharacterizedImage DecodeCharacterized(
        string path,
        LibRawHighlightMode highlight,
        LibRawFbddMode fbdd)
    {
        using var context = LibRawContext.Open(path);
        context.Unpack();
        var facts = RawCameraFactSnapshot.Copy(context.GetCameraFacts());
        context.ConfigureOutput(LibRawOutputConfiguration.LinearCameraNative(
            highlight,
            fbdd,
            halfSize: true));
        context.Process();
        using var processed = context.MakeProcessedImage();
        var characterization = CameraRgbCharacterization.Create(facts);
        var image = characterization.ImportRgb16(
            processed.AsSpan(),
            checked((int)processed.Description.Width),
            checked((int)processed.Description.Height));
        return new CharacterizedImage(characterization.Outcome, image);
    }

    private static double[,] ToDouble(float[,] values)
    {
        var result = new double[values.GetLength(0), values.GetLength(1)];
        for (var row = 0; row < values.GetLength(0); row++)
        for (var column = 0; column < values.GetLength(1); column++)
        {
            result[row, column] = values[row, column];
        }
        return result;
    }

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

    private static double MaximumDifference(double[,] first, double[,] second)
    {
        var maximum = 0d;
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            maximum = Math.Max(maximum, Math.Abs(
                first[row, column] - second[row, column]));
        }
        return maximum;
    }

    private sealed record DecodedImage(int Width, int Height, ushort[] Samples);
    private sealed record CharacterizedImage(
        CameraRgbCharacterizationOutcome Outcome,
        MagickImage Image);
}

using System.Runtime.InteropServices;
using HappyPhoton.LibRaw.Interop;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RawWorkingSpaceTests
{
    private const double NearClipFractionTolerance = 1e-4;

    private readonly ITestOutputHelper _output;

    public RawWorkingSpaceTests(ITestOutputHelper output) => _output = output;

    private static string CanonFixture => Path.Combine(
        GoldenTestPaths.AssetDirectory,
        "canon-eos-350d.cr2");

    [Fact]
    public void Rec2020Output_MatchesIndependentWorkingSpaceMatrix()
    {
        var srgb = Decode(LibRawOutputConfiguration.Linear(
            LibRawHighlightMode.Clip,
            LibRawFbddMode.Off,
            halfSize: true));
        var rec2020 = Decode(LibRawOutputConfiguration.LinearRec2020(
            LibRawHighlightMode.Clip,
            LibRawFbddMode.Off,
            halfSize: true));

        Assert.Equal(srgb.Width, rec2020.Width);
        Assert.Equal(srgb.Height, rec2020.Height);
        double totalError = 0;
        double maximumError = 0;
        var comparedChannels = 0;
        for (var offset = 0; offset < srgb.Samples.Length; offset += 3 * 97)
        {
            var source = new[]
            {
                srgb.Samples[offset] / (double)ushort.MaxValue,
                srgb.Samples[offset + 1] / (double)ushort.MaxValue,
                srgb.Samples[offset + 2] / (double)ushort.MaxValue
            };
            if (source.Any(value => value is < 0.01 or > 0.99)) continue;
            var expected = ChromaticAdaptation.Multiply(
                RgbColorSpaceMatrices.LinearSrgbToLinearRec2020,
                source);
            if (expected.Any(value => value is < 0.01 or > 0.99)) continue;
            for (var channel = 0; channel < 3; channel++)
            {
                var actual = rec2020.Samples[offset + channel] /
                    (double)ushort.MaxValue;
                var error = Math.Abs(actual - expected[channel]);
                totalError += error;
                maximumError = Math.Max(maximumError, error);
                comparedChannels++;
            }
        }

        Assert.True(comparedChannels > 1000);
        Assert.InRange(totalError / comparedChannels, 0, 2e-4);
        Assert.InRange(maximumError, 0, 0.003);
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

    [Theory]
    [InlineData("canon-eos-350d.cr2", 0.025364244276161376)]
    [InlineData("pentax-k-r.dng", 0.15691412317574743)]
    public void RawNearClip_PreservesPreWideDisplayBasisMeaning(
        string fileName,
        double frozenPreWide)
    {
        var path = Path.Combine(GoldenTestPaths.AssetDirectory, fileName);
        var baseline = Decode(path, LibRawOutputConfiguration.Linear(
            LibRawHighlightMode.Clip,
            LibRawFbddMode.Off,
            halfSize: false));
        var baselineNearClip = CalculateNearClip(baseline.Samples);
        Assert.InRange(
            Math.Abs(frozenPreWide - baselineNearClip),
            0,
            NearClipFractionTolerance);
        using var wide = new RawBaseLoader().LoadFullBase(
            new HappyPhoton.Models.ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                $"Raw fixture did not load: {fileName}");
        var actual = ClippingStatsCalculator.CalculateRawNearClip(wide);

        _output.WriteLine(
            $"{fileName}: pre-wide={baselineNearClip:R}; wide={actual:R}");
        Assert.InRange(Math.Abs(actual - baselineNearClip), 0, 5e-5);
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

    private static double CalculateNearClip(ushort[] samples)
    {
        var clipped = 0;
        for (var offset = 0; offset < samples.Length; offset += 3)
        {
            if (samples[offset] >= 64880 ||
                samples[offset + 1] >= 64880 ||
                samples[offset + 2] >= 64880)
            {
                clipped++;
            }
        }
        return clipped / (double)(samples.Length / 3);
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
}

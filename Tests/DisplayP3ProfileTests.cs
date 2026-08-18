using System.Buffers.Binary;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DisplayP3ProfileTests
{
    private static readonly double[][] Primaries =
    [
        [0.680, 0.320],
        [0.265, 0.690],
        [0.150, 0.060]
    ];

    private static readonly double[] D65 = [0.3127, 0.3290];

    private static readonly double[,] IccD65ToD50 =
    {
        { 1.04788603, 0.02291869, -0.05021606 },
        { 0.02958179, 0.99048358, -0.01707873 },
        { -0.00925190, 0.01507256, 0.75167814 }
    };

    [Fact]
    public void DisplayP3Matrix_AgreesWithPublishedPrimariesAndComposite()
    {
        var derived = ColorScienceMatrixAssertions.DeriveRgbToXyz(Primaries, D65);
        ColorScienceMatrixAssertions.AssertMatrixClose(
            RgbColorSpaceMatrices.LinearDisplayP3ToXyzD65DerivedExact,
            derived,
            2e-12,
            "Display P3 published primaries vs production");

        var composite = ChromaticAdaptation.Multiply(
            PrecisionColorCases.Invert(derived),
            RgbColorSpaceMatrices.LinearRec2020ToXyzD65DerivedExact);
        ColorScienceMatrixAssertions.AssertMatrixClose(
            RgbColorSpaceMatrices.LinearRec2020ToLinearDisplayP3,
            composite,
            6e-11,
            "Rec.2020 to Display P3 composite");
        var normalized = ChromaticAdaptation.NormalizeForRender(composite);
        Assert.InRange(
            normalized.Fold,
            1.343578252584330,
            1.343578252584335);
    }

    [Fact]
    public void EmbeddedDisplayP3Profile_HasMatchingPrimariesAndSrgbTransfer()
    {
        const double chromaticityTolerance = 1.1e-5;
        var bytes = OutputColorProfiles.Get(OutputColorSpace.DisplayP3).ToByteArray();
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(
                GoldenTestPaths.AssetDirectory,
                "DisplayP3-v4.icc")),
            bytes);

        var chad = ReadMatrix(bytes, "chad");
        ColorScienceMatrixAssertions.AssertMatrixClose(
            chad,
            IccD65ToD50,
            8e-6,
            "embedded profile chromatic adaptation");
        var colorants = new double[3, 3];
        foreach (var (tag, column) in new[]
        {
            ("rXYZ", 0),
            ("gXYZ", 1),
            ("bXYZ", 2)
        })
        {
            var xyz = ReadXyz(bytes, tag);
            for (var row = 0; row < 3; row++)
            {
                colorants[row, column] = xyz[row];
            }
        }
        var d65Colorants = ChromaticAdaptation.Multiply(
            PrecisionColorCases.Invert(chad),
            colorants);
        for (var column = 0; column < 3; column++)
        {
            var sum = d65Colorants[0, column] + d65Colorants[1, column] +
                d65Colorants[2, column];
            Assert.InRange(
                d65Colorants[0, column] / sum,
                Primaries[column][0] - chromaticityTolerance,
                Primaries[column][0] + chromaticityTolerance);
            Assert.InRange(
                d65Colorants[1, column] / sum,
                Primaries[column][1] - chromaticityTolerance,
                Primaries[column][1] + chromaticityTolerance);
        }

        foreach (var tag in new[] { "rTRC", "gTRC", "bTRC" })
        {
            var offset = FindTag(bytes, tag);
            Assert.Equal("para", ReadSignature(bytes, offset));
            Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(
                bytes.AsSpan(offset + 8, 2)));
            var expected = new[]
            {
                2.4,
                1 / 1.055,
                0.055 / 1.055,
                1 / 12.92,
                0.04045
            };
            for (var index = 0; index < expected.Length; index++)
            {
                Assert.InRange(
                    ReadFixed(bytes, offset + 12 + index * 4),
                    expected[index] - 5e-5,
                    expected[index] + 5e-5);
            }
        }
    }

    private static double[,] ReadMatrix(byte[] bytes, string tag)
    {
        var offset = FindTag(bytes, tag);
        var result = new double[3, 3];
        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            result[row, column] = ReadFixed(
                bytes,
                offset + 8 + (row * 3 + column) * 4);
        }
        return result;
    }

    private static double[] ReadXyz(byte[] bytes, string tag)
    {
        var offset = FindTag(bytes, tag);
        return Enumerable.Range(0, 3)
            .Select(index => ReadFixed(bytes, offset + 8 + index * 4))
            .ToArray();
    }

    private static int FindTag(byte[] bytes, string signature)
    {
        var count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(
            bytes.AsSpan(128, 4)));
        for (var index = 0; index < count; index++)
        {
            var entry = 132 + index * 12;
            if (ReadSignature(bytes, entry) == signature)
            {
                return checked((int)BinaryPrimitives.ReadUInt32BigEndian(
                    bytes.AsSpan(entry + 4, 4)));
            }
        }
        throw new InvalidOperationException($"ICC tag is missing: {signature}.");
    }

    private static double ReadFixed(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4)) / 65536.0;

    private static string ReadSignature(byte[] bytes, int offset) =>
        System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
}

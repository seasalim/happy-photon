using System.Security.Cryptography;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SoftProofBaselineFixtureTests
{
    private const string GenerateVariable = "HAPPY_PHOTON_GENERATE_SOFTPROOF_BASELINE";
    private static readonly string FixtureDirectory = Path.Combine(
        GoldenTestPaths.AssetDirectory, "softproof");

    [Fact]
    public void CommittedFixtures_MatchGeneratorAndPinnedHashes()
    {
        Assert.Contains("lcms", MagickVersionOutput);
        Assert.Equal(
            PinnedHashes.Keys.Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(FixtureDirectory)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal));
        foreach (var (name, expectedBytes) in SoftProofIccFixtureWriter.CreateProfiles())
        {
            Assert.Equal(expectedBytes, File.ReadAllBytes(Path.Combine(FixtureDirectory, name)));
        }

        foreach (var (name, expectedHash) in PinnedHashes)
        {
            var bytes = File.ReadAllBytes(Path.Combine(FixtureDirectory, name));
            Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(bytes)));
        }
    }

    [Fact]
    public void GenerateProfilesAndChart()
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable(GenerateVariable) != "1",
            $"Set {GenerateVariable}=1 to regenerate the committed inputs.");
        Directory.CreateDirectory(FixtureDirectory);
        foreach (var (name, bytes) in SoftProofIccFixtureWriter.CreateProfiles())
        {
            File.WriteAllBytes(Path.Combine(FixtureDirectory, name), bytes);
        }

        var pixels = new byte[64 * 64 * 3];
        for (var index = 0; index < 4096; index++)
        {
            pixels[index * 3] = checked((byte)((index >> 8) * 17));
            pixels[index * 3 + 1] = checked((byte)(((index >> 4) & 15) * 17));
            pixels[index * 3 + 2] = checked((byte)((index & 15) * 17));
        }
        var settings = new PixelReadSettings(64, 64, StorageType.Char, PixelMapping.RGB);
        settings.ReadSettings.ColorSpace = ColorSpace.sRGB;
        using var chart = new MagickImage(pixels, settings)
        {
            Depth = 8,
            Format = MagickFormat.Png
        };
        chart.Write(Path.Combine(FixtureDirectory, "softproof-chart.png"));
    }

    private const string MagickVersionOutput = """
        Version: ImageMagick 7.1.2-30 Q16-HDRI x64 344e905:20260823 https://imagemagick.org
        Copyright: (C) 1999 ImageMagick Studio LLC
        License: https://imagemagick.org/license/
        Features: Channel-masks(64-bit) Cipher DPC HDRI Modules OpenCL OpenMP(2.0)
        Delegates (built-in): bzlib cairo freetype gslib heic jng jp2 jpeg jxl lcms lqr lzma openexr pangocairo png ps raqm raw rsvg tiff webp xml zip zlib
        Compiler: Visual Studio 2026 (195136256)
        """;

    // Working directory: Tests/assets/softproof
    // magick softproof-chart.png -intent Relative -define profile:black-point-compensation=false -profile softproof-srgb.icc -profile softproof-srgb.icc oracle-srgb.png
    // magick softproof-chart.png -intent Relative -define profile:black-point-compensation=false -profile softproof-srgb.icc -profile softproof-p3-gamma22.icc oracle-p3-gamma22.png
    // magick softproof-chart.png -intent Relative -define profile:black-point-compensation=false -profile softproof-srgb.icc -profile softproof-p3-curv1024.icc oracle-p3-curv1024.png
    private static readonly IReadOnlyDictionary<string, string> PinnedHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["softproof-srgb.icc"] = "0931A3EADADDDAC54FD7B2EF1A4E245A603D6A932BED82C9563E23443F89DB36",
            ["softproof-p3-gamma22.icc"] = "4F23C4D65DA7BB7F26FAD501DB3603A388C3E60AFD41E3DF00C9434A194826B5",
            ["softproof-p3-curv1024.icc"] = "0989762D4A0259BD2DF575924927B2D6892C9A027DBADB97D4EC4411361728ED",
            ["softproof-p3-a2b1.icc"] = "375F2DA2096929EF801E04C104F1283D6C3E90D215DE9B6A80C594618A7D5751",
            ["softproof-p3-d2b0.icc"] = "8E9BB45A8ADC13B3280BBF54A6342812C3C8B37CD45DCD8BD663EB838D8AA0E3",
            ["softproof-p3-mhc2.icc"] = "915510939CA070A2C38E7BD1A4EAA028A3BB2ADF09180000AFF126C0C432A437",
            ["softproof-chart.png"] = "C0F9C671B8B7FD39E32C438BA36F4B39A20927E849DA0EAD4284AA7BE0E9ECE0",
            ["oracle-srgb.png"] = "55A8FBFDCE23B3D4784D641ACBA9534579F7A098F700726EC8BB7538B5216F95",
            ["oracle-p3-gamma22.png"] = "4B5FA6FCE6AE6DCBAE0ECB06461EDE4F9F1915AC94953D04D78F418F31291250",
            ["oracle-p3-curv1024.png"] = "EE802DEBFD66B9808505A996586A3D4FAC158BDCD8005B349ED9934A83E09F19"
        };
}

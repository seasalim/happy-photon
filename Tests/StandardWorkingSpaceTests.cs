using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class StandardWorkingSpaceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonWorkingSpaceTests_{Guid.NewGuid():N}");

    public StandardWorkingSpaceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void MagickSrgbProfile_NormalizesToRec2020OracleWithoutDoubleTransfer()
    {
        var path = WriteFixture(
            "srgb-native.png",
            ColorProfiles.SRGB,
        [
            255, 0, 0,
            0, 255, 0,
            0, 0, 255,
            128, 128, 128
        ]);

        using var result = Load(path);

        AssertPixelsClose(result.Pixels,
        [
            41117, 4528, 1074,
            21580, 60262, 5768,
            2839, 745, 58693,
            14146, 14146, 14146
        ], tolerance: 16);
    }

    [Fact]
    public void NativeDisplayP3Gamut_SurvivesNormalizationToRec2020()
    {
        var profile = new ColorProfile(Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "DisplayP3-v4.icc"));
        var path = WriteFixture(
            "display-p3-native.png",
            profile,
        [
            255, 0, 0,
            0, 255, 0,
            255, 128, 0,
            128, 128, 128
        ]);

        using var result = Load(path);

        AssertPixelsClose(result.Pixels,
        [
            49402, 2998, 0,
            13015, 61719, 1154,
            52212, 16321, 170,
            14146, 14146, 14146
        ], tolerance: 4);
        var pixels = ReadPixels(result.Pixels);
        Assert.True(pixels[0] < ushort.MaxValue);
        Assert.True(pixels[4] < ushort.MaxValue);
    }

    [Fact]
    public void ThumbnailSrgbProxy_ConvertsToWorkingSpaceBeforeRendering()
    {
        using var image = new MagickImage(MagickColors.Red, 1, 1)
        {
            ColorSpace = ColorSpace.sRGB,
            Depth = 8
        };

        ThumbnailRenderer.ConvertSrgbProxyToWorking(image);

        Assert.Equal(ColorSpace.RGB, image.ColorSpace);
        AssertPixelsClose(image, [41117, 4528, 1074], tolerance: 3);
    }

    [Fact]
    public void UneditedSrgbJpeg_RoundTripsToDisplayWithinOneEightBitCode()
    {
        var path = Path.Combine(
            GoldenTestPaths.AssetDirectory,
            "srgb-reference.jpg");
        using var source = new MagickImage(path);
        if (source.GetColorProfile() is { } sourceProfile)
        {
            source.TransformColorSpace(sourceProfile, ColorProfiles.SRGB);
        }
        source.AutoOrient();
        using var baseImage = new StandardBaseLoader().LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                "sRGB identity fixture did not load.");
        using var rendered = new RenderPipeline().Render(new RenderRequest(
            baseImage,
            new EditSettings(),
            RenderIntent.Export,
            null,
            new RenderOptions(false, false)));
        using var sourcePixels = source.GetPixels();
        using var renderedPixels = rendered.Image.GetPixels();
        var expected = sourcePixels.ToByteArray(PixelMapping.RGB) ?? [];
        var actual = renderedPixels.ToByteArray(PixelMapping.RGB) ?? [];

        Assert.Equal(expected.Length, actual.Length);
        var maximum = expected.Zip(actual, (left, right) =>
            Math.Abs(left - right)).Max();
        Assert.InRange(maximum, 0, 1);
    }

    private BaseImage Load(string path) =>
        new StandardBaseLoader().LoadFullBase(
            new ImageFile(path),
            BaseDecodeSettings.Default,
            CancellationToken.None) ?? throw new InvalidOperationException(
                $"Fixture did not load: {path}");

    private string WriteFixture(
        string fileName,
        IColorProfile profile,
        byte[] codes)
    {
        var path = Path.Combine(_directory, fileName);
        var settings = new PixelReadSettings(
            (uint)(codes.Length / 3),
            1,
            StorageType.Char,
            PixelMapping.RGB);
        settings.ReadSettings.ColorSpace = ColorSpace.sRGB;
        using var image = new MagickImage(codes, settings)
        {
            Depth = 8,
            Format = MagickFormat.Png
        };
        image.SetProfile(profile);
        image.Write(path);
        return path;
    }

    private static void AssertPixelsClose(
        MagickImage image,
        ushort[] expected,
        int tolerance)
    {
        var actual = ReadPixels(image);
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.InRange(
                actual[index],
                Math.Max(0, expected[index] - tolerance),
                Math.Min(ushort.MaxValue, expected[index] + tolerance));
        }
    }

    private static ushort[] ReadPixels(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Could not read RGB pixels.");

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

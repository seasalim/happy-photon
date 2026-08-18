using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WideGamutExportTests : IDisposable
{
    private const int PatchSize = 32;
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonWideGamut_{Guid.NewGuid():N}");

    public WideGamutExportTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(ExportFormat.Jpeg, ".jpg", 4)]
    [InlineData(ExportFormat.Png, ".png", 1)]
    [InlineData(ExportFormat.Webp, ".webp", 4)]
    public async Task DisplayP3Export_EmbedsMatchingProfileAndRecoversNativeP3(
        ExportFormat format,
        string extension,
        int tolerance)
    {
        var source = WriteFixture(
            "native-p3.png",
            new ColorProfile(ProfilePath),
            [
                [255, 0, 0],
                [0, 255, 0],
                [255, 128, 0],
                [128, 128, 128]
            ]);
        var outputFolder = Path.Combine(_directory, $"p3-{format}");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = format,
            Quality = 100,
            OutputSharpening = false,
            OutputColorSpace = OutputColorSpace.DisplayP3
        };

        await CreateService().ExportBatchAsync([new ImageFile(source)], settings);

        var path = Path.Combine(outputFolder, $"native-p3{extension}");
        using var exported = ReadExport(path);
        Assert.Equal(
            File.ReadAllBytes(ProfilePath),
            exported.GetColorProfile()!.ToByteArray());
        AssertPatchCodes(exported,
        [
            [255, 0, 4],
            [0, 255, 0],
            [255, 128, 0],
            [128, 128, 128]
        ], tolerance);
    }

    [Fact]
    public async Task NativeDisplayP3Red_SurvivesBeyondSrgbExport()
    {
        var source = WriteFixture(
            "gamut-sentinel.png",
            new ColorProfile(ProfilePath),
            [[255, 0, 0]]);
        var p3 = await ExportPng(source, OutputColorSpace.DisplayP3, "sentinel-p3");
        var srgb = await ExportPng(source, OutputColorSpace.Srgb, "sentinel-srgb");
        using var p3Image = ReadExport(p3);
        using var srgbImage = ReadExport(srgb);
        var p3Code = ReadPatch(p3Image, 0);
        var srgbCode = ReadPatch(srgbImage, 0);

        Assert.InRange(p3Code[0], 254, 255);
        Assert.InRange(p3Code[1], 0, 1);
        Assert.InRange(p3Code[2], 3, 5);
        Assert.Equal([255, 0, 0], srgbCode);

        srgbImage.TransformColorSpace(ColorProfiles.SRGB, new ColorProfile(ProfilePath));
        var clippedAsP3 = ReadPatch(srgbImage, 0);
        Assert.True(
            clippedAsP3[0] < 240 && clippedAsP3[1] > 40,
            $"sRGB-clipped red unexpectedly retained native P3 codes: " +
            $"{string.Join(',', clippedAsP3)}.");
    }

    [Fact]
    public async Task IntersectionGamutExports_AgreeThroughEmbeddedProfiles()
    {
        var source = WriteFixture(
            "intersection.png",
            ColorProfiles.SRGB,
            [
                [200, 50, 30],
                [30, 200, 80],
                [40, 80, 200],
                [128, 128, 128]
            ]);
        var srgb = await ExportPng(source, OutputColorSpace.Srgb, "intersection-srgb");
        var p3 = await ExportPng(source, OutputColorSpace.DisplayP3, "intersection-p3");
        using var srgbImage = ReadExport(srgb);
        using var p3Image = ReadExport(p3);

        var comparison = GoldenImageComparer.Compare(
            srgbImage,
            p3Image,
            GoldenComparisonDomain.DisplaySrgb);

        Assert.True(
            comparison.MeanDeltaE <= 0.5,
            $"Common-space mean ΔE was {comparison.MeanDeltaE:F3}.");
    }

    private async Task<string> ExportPng(
        string source,
        OutputColorSpace outputColorSpace,
        string folderName)
    {
        var outputFolder = Path.Combine(_directory, folderName);
        await CreateService().ExportBatchAsync(
            [new ImageFile(source)],
            new ExportSettings
            {
                OutputFolder = outputFolder,
                Format = ExportFormat.Png,
                OutputSharpening = false,
                OutputColorSpace = outputColorSpace
            });
        return Path.Combine(
            outputFolder,
            $"{Path.GetFileNameWithoutExtension(source)}.png");
    }

    private string WriteFixture(
        string fileName,
        IColorProfile profile,
        byte[][] colors)
    {
        var path = Path.Combine(_directory, fileName);
        var bytes = new byte[colors.Length * PatchSize * PatchSize * 3];
        for (var y = 0; y < PatchSize; y++)
        for (var x = 0; x < colors.Length * PatchSize; x++)
        {
            var color = colors[x / PatchSize];
            var offset = (y * colors.Length * PatchSize + x) * 3;
            color.CopyTo(bytes, offset);
        }
        var settings = new PixelReadSettings(
            (uint)(colors.Length * PatchSize),
            PatchSize,
            StorageType.Char,
            PixelMapping.RGB);
        settings.ReadSettings.ColorSpace = ColorSpace.sRGB;
        using var image = new MagickImage(bytes, settings)
        {
            Depth = 8,
            Format = MagickFormat.Png
        };
        image.SetProfile(profile);
        image.Write(path);
        return path;
    }

    private static void AssertPatchCodes(
        MagickImage image,
        byte[][] expected,
        int tolerance)
    {
        for (var patch = 0; patch < expected.Length; patch++)
        {
            var actual = ReadPatch(image, patch);
            for (var channel = 0; channel < 3; channel++)
            {
                Assert.InRange(
                    actual[channel],
                    Math.Max(0, expected[patch][channel] - tolerance),
                    Math.Min(255, expected[patch][channel] + tolerance));
            }
        }
    }

    private static byte[] ReadPatch(MagickImage image, int patch)
    {
        using var pixels = image.GetPixels();
        var bytes = pixels.ToByteArray(PixelMapping.RGB)!;
        var x = patch * PatchSize + PatchSize / 2;
        var y = PatchSize / 2;
        var offset = checked((y * (int)image.Width + x) * 3);
        return bytes[offset..(offset + 3)];
    }

    private static MagickImage ReadExport(string path)
    {
        var settings = new MagickReadSettings();
        if (Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            settings.SetDefine(MagickFormat.Png, "preserve-iCCP", "true");
        }
        return new MagickImage(path, settings);
    }

    private static ImageExportService CreateService() => new(
        new RenderPipeline(),
        new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()),
        new ExportMetadataService());

    private static string ProfilePath => Path.Combine(
        GoldenTestPaths.AssetDirectory,
        "DisplayP3-v4.icc");

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

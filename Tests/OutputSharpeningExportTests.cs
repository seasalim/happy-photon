using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class OutputSharpeningExportTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-output-sharpen-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task Export_OutputSharpeningAffectsOnlyDownsizedVariant()
    {
        var sourcePath = WriteEdgeSource();
        var enabledFolder = Path.Combine(_root, "enabled");
        var disabledFolder = Path.Combine(_root, "disabled");

        await ExportAsync(sourcePath, enabledFolder, outputSharpening: true);
        await ExportAsync(sourcePath, disabledFolder, outputSharpening: false);

        Assert.Equal(
            ReadRgb(Path.Combine(enabledFolder, "hi-res", "edge.png")),
            ReadRgb(Path.Combine(disabledFolder, "hi-res", "edge.png")));
        Assert.NotEqual(
            ReadRgb(Path.Combine(enabledFolder, "web", "edge.png")),
            ReadRgb(Path.Combine(disabledFolder, "web", "edge.png")));
    }

    [Fact]
    public async Task Export_SizedVariantThatDoesNotShrinkIsNotSharpened()
    {
        var sourcePath = WriteEdgeSource();
        var enabledFolder = Path.Combine(_root, "unresized-enabled");
        var disabledFolder = Path.Combine(_root, "unresized-disabled");

        await ExportAsync(
            sourcePath,
            enabledFolder,
            outputSharpening: true,
            hiRes: false,
            webMaxSize: 500);
        await ExportAsync(
            sourcePath,
            disabledFolder,
            outputSharpening: false,
            hiRes: false,
            webMaxSize: 500);

        Assert.Equal(
            ReadRgb(Path.Combine(enabledFolder, "edge.png")),
            ReadRgb(Path.Combine(disabledFolder, "edge.png")));
    }

    [Fact]
    public async Task Export_DuplicateSizedVariantsAreNotDoubleSharpened()
    {
        var sourcePath = WriteEdgeSource();
        var outputFolder = Path.Combine(_root, "duplicates");
        var service = new ImageExportService(
            new RenderPipeline(),
            new StandardBaseLoader(),
            new ExportMetadataService());

        await service.ExportBatchAsync(
            [new ImageFile(sourcePath)],
            new ExportSettings
            {
                OutputFolder = outputFolder,
                Format = ExportFormat.Png
            },
            [
                new ExportVariant("first", 200),
                new ExportVariant("second", 200)
            ],
            useSubfolders: true);

        Assert.Equal(
            ReadRgb(Path.Combine(outputFolder, "first", "edge.png")),
            ReadRgb(Path.Combine(outputFolder, "second", "edge.png")));
    }

    private static async Task ExportAsync(
        string sourcePath,
        string outputFolder,
        bool outputSharpening,
        bool hiRes = true,
        int webMaxSize = 200)
    {
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Png,
            ExportHiRes = hiRes,
            ExportWeb = true,
            WebMaxSize = webMaxSize,
            OutputSharpening = outputSharpening
        };
        var service = new ImageExportService(
            new RenderPipeline(),
            new StandardBaseLoader(),
            new ExportMetadataService());

        var count = await service.ExportBatchAsync(
            [new ImageFile(sourcePath)],
            settings);

        Assert.Equal(1, count);
    }

    private string WriteEdgeSource()
    {
        const int width = 400;
        const int height = 200;
        using var image = new MagickImage(
            MagickColors.Black,
            width,
            height)
        {
            Depth = 16,
            ColorSpace = ColorSpace.sRGB,
            Format = MagickFormat.Png
        };
        using (var pixels = image.GetPixels())
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var value = (ushort)(x < width / 2 ? 16000 : 49000);
                    pixels.SetPixel(x, y, [value, value, value]);
                }
            }
        }

        var path = Path.Combine(_root, "edge.png");
        image.Write(path);
        return path;
    }

    private static ushort[] ReadRgb(string path)
    {
        using var image = new MagickImage(path);
        return image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
            throw new InvalidOperationException("Unable to read output pixels.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

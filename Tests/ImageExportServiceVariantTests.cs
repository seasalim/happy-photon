using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ImageExportServiceVariantTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonExportTests_{Guid.NewGuid():N}");

    public ImageExportServiceVariantTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public async Task ExportBatch_WritesProgressivelySizedWebpVariants()
    {
        var sourcePath = WriteSourceImage();
        var outputFolder = Path.Combine(_tempDirectory, "exports");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Webp,
            ExportWeb = true,
            ExportSmall = true,
            WebMaxSize = 200,
            SmallMaxSize = 100
        };
        var service = new ImageExportService(
            new EditApplicationService(), new MagickNetRawService());

        var count = await service.ExportBatchAsync([new ImageFile(sourcePath)], settings);

        Assert.Equal(1, count);
        AssertImage(Path.Combine(outputFolder, "hi-res", "source.webp"), 400, 200);
        AssertImage(Path.Combine(outputFolder, "web", "source.webp"), 200, 100);
        AssertImage(Path.Combine(outputFolder, "small", "source.webp"), 100, 50);
    }

    [Fact]
    public async Task ExportBatch_SinglePngVariantStaysFlat()
    {
        var sourcePath = WriteSourceImage();
        var outputFolder = Path.Combine(_tempDirectory, "exports");
        var settings = new ExportSettings
        {
            OutputFolder = outputFolder,
            Format = ExportFormat.Png
        };
        var service = new ImageExportService(
            new EditApplicationService(), new MagickNetRawService());

        await service.ExportBatchAsync([new ImageFile(sourcePath)], settings);

        AssertImage(Path.Combine(outputFolder, "source.png"), 400, 200);
        Assert.False(Directory.Exists(Path.Combine(outputFolder, "hi-res")));
    }

    private string WriteSourceImage()
    {
        var path = Path.Combine(_tempDirectory, "source.png");
        using var image = new MagickImage(MagickColors.Orange, 400, 200);
        image.Write(path);
        return path;
    }

    private static void AssertImage(string path, uint width, uint height)
    {
        Assert.True(File.Exists(path));
        using var image = new MagickImage(path);
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}

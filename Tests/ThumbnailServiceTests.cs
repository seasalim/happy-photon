using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ThumbnailServiceTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonThumbnailTests_{Guid.NewGuid():N}");

    public ThumbnailServiceTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LoadUneditedThumbnailAsync_IgnoresDisplayEdits()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, "source.jpg");
        using (var source = new MagickImage(MagickColors.Gray, 400, 200))
        {
            source.Write(sourcePath, MagickFormat.Jpeg);
        }

        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        await catalog.InitializeAsync();
        await using var imageService = new ImageService(catalog);
        var image = new ImageFile(sourcePath)
        {
            EditSettings = new EditSettings { Exposure = 3 }
        };

        using var unedited = await imageService.LoadUneditedThumbnailAsync(
            image, CancellationToken.None);
        using var edited = await imageService.LoadThumbnailAsync(
            image, CancellationToken.None);

        Assert.NotNull(unedited);
        Assert.NotNull(edited);
        var stats = new ImageStatsService();
        var uneditedStats = stats.Compute(
            BitmapConversionService.CreateEncodedSnapshot(unedited));
        var editedStats = stats.Compute(
            BitmapConversionService.CreateEncodedSnapshot(edited));
        Assert.True(uneditedStats.MeanLuminance < editedStats.MeanLuminance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}

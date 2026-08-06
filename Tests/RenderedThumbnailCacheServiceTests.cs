using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class RenderedThumbnailCacheServiceTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonRenderedThumbCache_{Guid.NewGuid():N}");

    public RenderedThumbnailCacheServiceTests(AvaloniaTestFixture fixture) =>
        _fixture = fixture;

    [WindowsFact]
    public async Task MatchingHashLoadsQuality85Jpeg()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync();
        using (catalog)
        {
            var cache = new RenderedThumbnailCacheService(catalog);
            using var source = new MagickImage(MagickColors.Orange, 150, 100);
            using var bitmap = BitmapConversionService.ConvertToBitmap(source)!;

            cache.QueueSaveToCache(file, bitmap, "matching-hash");
            await cache.DisposeAsync();

            var path = catalog.GetRenderedThumbnailPath(file.CatalogId);
            using var encoded = new MagickImage(path);
            Assert.Equal(85u, encoded.Quality);
            Assert.Equal(
                "matching-hash",
                File.ReadAllText(Path.ChangeExtension(path, ".meta")));

            var reader = new RenderedThumbnailCacheService(catalog);
            using var loaded = reader.LoadMatching(file, "matching-hash");
            Assert.NotNull(loaded);
            Assert.Equal(150, loaded!.PixelSize.Width);
            Assert.Null(reader.LoadMatching(file, "other-hash"));
            await reader.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task NewerSourceRejectsRenderedThumbnail()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync();
        using (catalog)
        {
            var cache = new RenderedThumbnailCacheService(catalog);
            using var source = new MagickImage(MagickColors.Blue, 30, 20);
            using var bitmap = BitmapConversionService.ConvertToBitmap(source)!;
            cache.QueueSaveToCache(file, bitmap, "hash");
            await cache.DisposeAsync();

            File.SetLastWriteTimeUtc(
                file.FilePath,
                DateTime.UtcNow.AddMinutes(2));
            var reader = new RenderedThumbnailCacheService(catalog);
            Assert.Null(reader.LoadMatching(file, "hash"));
            await reader.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task SourceFolderScanCannotEvictRenderedPromotionQueue()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync();
        using (catalog)
        {
            var gate = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var renderedCache = new RenderedThumbnailCacheService(
                catalog,
                1,
                gate.Task,
                TimeSpan.FromSeconds(5));
            var sourceCache = new ThumbnailCacheService(catalog);
            using var image = new MagickImage(MagickColors.Green, 20, 10);
            using var bitmap = BitmapConversionService.ConvertToBitmap(image)!;

            renderedCache.QueueSaveToCache(file, bitmap, "promotion");
            for (var index = 0; index < 300; index++)
            {
                sourceCache.QueueSaveToCache(file, bitmap);
            }
            await sourceCache.DisposeAsync();
            gate.SetResult();
            await renderedCache.DisposeAsync();

            var path = catalog.GetRenderedThumbnailPath(file.CatalogId);
            Assert.True(File.Exists(path));
            Assert.Equal(
                "promotion",
                File.ReadAllText(Path.ChangeExtension(path, ".meta")));
        }
    }

    private async Task<(CatalogService Catalog, ImageFile File)> CreateFileAsync()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"{Guid.NewGuid():N}.dng");
        await File.WriteAllBytesAsync(path, [1]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
        var catalog = new CatalogService(Path.Combine(_root, Guid.NewGuid().ToString("N")));
        await catalog.InitializeAsync();
        var file = new ImageFile(path);
        await file.EnsureCatalogIdAsync(catalog);
        return (catalog, file);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

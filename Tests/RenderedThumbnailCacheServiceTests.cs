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

    // The production drain timeout bounds app shutdown; these tests assert on
    // the drained result, so the drain gets the standard wait ceiling instead.
    private static RenderedThumbnailCacheService CreateWriter(CatalogService catalog) =>
        new(catalog, 8, Task.CompletedTask, TestWaits.Condition);

    [WindowsFact]
    public async Task MatchingHashLoadsQuality85Jpeg()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync();
        using (catalog)
        {
            var cache = CreateWriter(catalog);
            using var source = new MagickImage(MagickColors.Orange, 150, 100);
            using var bitmap = BitmapConversionService.ConvertToBitmap(source)!;

            cache.QueueSaveToCache(file, bitmap, "matching-hash");
            await cache.DisposeAsync();

            var path = catalog.GetRenderedThumbnailPath(file.CatalogId);
            using var encoded = new MagickImage(path);
            Assert.Equal(85u, encoded.Quality);
            Assert.True(RenderedThumbnailMetadata.TryRead(
                Path.ChangeExtension(path, ".meta"),
                path,
                out var metadata));
            Assert.Equal(RenderedThumbnailMetadata.CurrentVersion, metadata.Version);
            Assert.Equal("matching-hash", metadata.SettingsHash);
            Assert.Equal(150, metadata.PixelWidth);
            Assert.Equal(100, metadata.PixelHeight);

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
            var cache = CreateWriter(catalog);
            using var source = new MagickImage(MagickColors.Blue, 30, 20);
            using var bitmap = BitmapConversionService.ConvertToBitmap(source)!;
            cache.QueueSaveToCache(file, bitmap, "hash");
            await cache.DisposeAsync();

            var reader = new RenderedThumbnailCacheService(catalog);
            using (var loaded = reader.LoadMatching(file, "hash"))
            {
                Assert.NotNull(loaded);
            }

            File.SetLastWriteTimeUtc(
                file.FilePath,
                DateTime.UtcNow.AddMinutes(2));
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
                TestWaits.Condition);
            var sourceCache = new ThumbnailCacheService(
                catalog,
                256,
                Task.CompletedTask,
                TestWaits.Condition);
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
            Assert.True(RenderedThumbnailMetadata.TryRead(
                Path.ChangeExtension(path, ".meta"),
                path,
                out var metadata));
            Assert.Equal("promotion", metadata.SettingsHash);
        }
    }

    [WindowsFact]
    public async Task LegacyHashSidecarLoadsWithDimensionsInferredFromJpeg()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync();
        using (catalog)
        {
            var path = catalog.GetRenderedThumbnailPath(file.CatalogId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            TestImages.WriteJpeg(path, MagickColors.Orange, 150, 100);
            File.WriteAllText(Path.ChangeExtension(path, ".meta"), "legacy-hash");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);

            await using var cache = new RenderedThumbnailCacheService(catalog);
            using var loaded = cache.LoadMatching(file, "legacy-hash");

            Assert.NotNull(loaded);
            Assert.Equal(150, loaded!.PixelSize.Width);
        }
    }

    [WindowsFact]
    public async Task MatchingHashWritesAreMonotonicByLongEdge()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync();
        using (catalog)
        {
            var cache = CreateWriter(catalog);
            using var largeImage = new MagickImage(MagickColors.Orange, 512, 341);
            using var smallImage = new MagickImage(MagickColors.Blue, 150, 100);
            using var large = BitmapConversionService.ConvertToBitmap(largeImage)!;
            using var small = BitmapConversionService.ConvertToBitmap(smallImage)!;

            cache.QueueSaveToCache(file, large, "hash");
            cache.QueueSaveToCache(file, small, "hash");
            await cache.DisposeAsync();

            Assert.True(JpegDimensions.TryRead(
                catalog.GetRenderedThumbnailPath(file.CatalogId),
                out var dimensions));
            Assert.Equal(512, Math.Max(dimensions.Width, dimensions.Height));
        }
    }

    [WindowsFact]
    public async Task MatchingUndersizedEntryRemainsAccuratePlaceholder()
    {
        _fixture.RequireWindows();
        var (catalog, file) = await CreateFileAsync();
        using (catalog)
        {
            var writer = CreateWriter(catalog);
            using var source = new MagickImage(MagickColors.Orange, 150, 100);
            using var bitmap = BitmapConversionService.ConvertToBitmap(source)!;
            writer.QueueSaveToCache(file, bitmap, "hash");
            await writer.DisposeAsync();

            await using var reader = new RenderedThumbnailCacheService(catalog);
            using var loaded = reader.LoadMatching(
                file,
                "hash",
                ThumbnailSizeRequest.For(LibraryThumbnailSize.Large),
                out var satisfiesMinimum);

            Assert.NotNull(loaded);
            Assert.False(satisfiesMinimum);
            Assert.Equal(150, loaded!.PixelSize.Width);
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

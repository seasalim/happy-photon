using System.Diagnostics;
using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class ThumbnailCacheServiceTests : IDisposable
{
    private readonly AvaloniaTestFixture _fixture;
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonCacheTests_{Guid.NewGuid():N}");

    public ThumbnailCacheServiceTests(AvaloniaTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task QueueSaveToCache_DropsOldestWritesWhenCapacityIsReached()
    {
        _fixture.RequireWindows();
        var sourcePath = CreateSource();
        using var bitmap = JpegThumbnailDecoder.Decode(
            sourcePath, 150, CancellationToken.None);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ThumbnailCacheService(
            catalog, 3, gate.Task, TimeSpan.FromSeconds(2));
        var images = Enumerable.Range(1, 5)
            .Select(id => new ImageFile(sourcePath) { CatalogId = id })
            .ToArray();

        try
        {
            foreach (var image in images)
            {
                cache.QueueSaveToCache(image, bitmap);
            }
            gate.SetResult();

            await cache.DisposeAsync();

            Assert.False(File.Exists(cache.GetCachePath(images[0])));
            Assert.False(File.Exists(cache.GetCachePath(images[1])));
            Assert.All(images[2..], image =>
                Assert.True(File.Exists(cache.GetCachePath(image))));
        }
        finally
        {
            gate.TrySetResult();
            await cache.ProcessingTask;
            await cache.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_ReturnsWhenWriterExceedsShutdownTimeout()
    {
        _fixture.RequireWindows();
        var sourcePath = CreateSource();
        using var bitmap = JpegThumbnailDecoder.Decode(
            sourcePath, 150, CancellationToken.None);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ThumbnailCacheService(
            catalog, 3, gate.Task, TimeSpan.FromMilliseconds(50));
        var image = new ImageFile(sourcePath) { CatalogId = 1 };

        try
        {
            cache.QueueSaveToCache(image, bitmap);
            var elapsed = Stopwatch.StartNew();

            await cache.DisposeAsync();

            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1));
            Assert.False(File.Exists(cache.GetCachePath(image)));
        }
        finally
        {
            gate.TrySetResult();
            await cache.ProcessingTask;
            await cache.DisposeAsync();
        }
    }

    [Fact]
    public async Task QueueSaveToCache_PersistsAtomicallyAndCleansTemporaryFile()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, "source.jpg");
        using (var source = new MagickImage(MagickColors.Red, 400, 200))
        {
            source.Write(sourcePath, MagickFormat.Jpeg);
        }

        using var bitmap = JpegThumbnailDecoder.Decode(
            sourcePath, 150, CancellationToken.None);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var cache = new ThumbnailCacheService(catalog);
        var firstImage = new ImageFile(sourcePath) { CatalogId = 1 };
        var secondImage = new ImageFile(sourcePath) { CatalogId = 2 };
        try
        {
            cache.QueueSaveToCache(firstImage, bitmap);
            cache.QueueSaveToCache(secondImage, bitmap);
            var firstCachePath = cache.GetCachePath(firstImage);
            var secondCachePath = cache.GetCachePath(secondImage);

            await cache.DisposeAsync();

            Assert.True(new FileInfo(firstCachePath).Length > 0);
            Assert.True(new FileInfo(secondCachePath).Length > 0);
            Assert.Equal(
                new byte[] { 0xff, 0xd8, 0xff },
                File.ReadAllBytes(firstCachePath).Take(3).ToArray());
            Assert.True(cache.IsCacheValid(firstImage));
            Assert.True(cache.IsCacheValid(secondImage));
            var temporaryDirectory = Path.Combine(catalog.CatalogPath, "assets", "tmp");
            Assert.Empty(Directory.GetFiles(temporaryDirectory));
        }
        finally
        {
            await cache.DisposeAsync();
        }
    }

    [Fact]
    public async Task LoadFromCache_LazilyMigratesLegacyPngBytes()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, "source.jpg");
        using (var source = new MagickImage(MagickColors.Red, 400, 200))
        {
            source.Write(sourcePath, MagickFormat.Jpeg);
        }

        using var bitmap = JpegThumbnailDecoder.Decode(
            sourcePath, 150, CancellationToken.None);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var cache = new ThumbnailCacheService(catalog);
        var imageFile = new ImageFile(sourcePath) { CatalogId = 1 };
        var cachePath = cache.GetCachePath(imageFile);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        bitmap.Save(cachePath);
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e },
            File.ReadAllBytes(cachePath).Take(3).ToArray());

        using var loaded = cache.LoadFromCache(imageFile);
        await cache.DisposeAsync();

        Assert.NotNull(loaded);
        Assert.Equal(new byte[] { 0xff, 0xd8, 0xff },
            File.ReadAllBytes(cachePath).Take(3).ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateSource()
    {
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, "source.jpg");
        using var source = new MagickImage(MagickColors.Red, 400, 200);
        source.Write(sourcePath, MagickFormat.Jpeg);
        return sourcePath;
    }
}

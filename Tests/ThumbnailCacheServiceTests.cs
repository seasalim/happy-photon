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

    // The production drain timeout bounds app shutdown; these tests assert on
    // the drained result, so the drain gets the standard wait ceiling instead.
    private static ThumbnailCacheService CreateWriter(CatalogService catalog) =>
        new(catalog, 256, Task.CompletedTask, TestWaits.Condition);

    [WindowsFact]
    public async Task QueueSaveToCache_DropsOldestWritesWhenCapacityIsReached()
    {
        _fixture.RequireWindows();
        var sourcePath = CreateSource();
        using var bitmap = JpegThumbnailDecoder.Decode(
            sourcePath, 150, CancellationToken.None);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ThumbnailCacheService(
            catalog, 3, gate.Task, TestWaits.Condition);
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

    [WindowsFact]
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

            await cache.DisposeAsync();

            // Returning while the writer is still gated is the whole claim:
            // dispose abandoned the drain instead of waiting the write out.
            Assert.False(cache.ProcessingTask.IsCompleted);
            Assert.False(File.Exists(cache.GetCachePath(image)));
        }
        finally
        {
            gate.TrySetResult();
            await cache.ProcessingTask;
            await cache.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task PendingWrites_IncludesWriterInHandAfterDequeue()
    {
        _fixture.RequireWindows();
        var sourcePath = CreateSource();
        using var bitmap = JpegThumbnailDecoder.Decode(
            sourcePath, 150, CancellationToken.None);
        using var catalog = new CatalogService(Path.Combine(
            _tempDirectory, "writer-in-hand-catalog"));
        var writerGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = new ThumbnailCacheService(
            catalog,
            2,
            Task.CompletedTask,
            TestWaits.Condition,
            writerGate.Task);
        var image = new ImageFile(sourcePath) { CatalogId = 1 };

        try
        {
            cache.QueueSaveToCache(image, bitmap);
            Assert.True(SpinWait.SpinUntil(
                () => cache.WriterInHandCount == 1,
                TestWaits.Condition));

            Assert.Equal(1, cache.PendingWrites);
            writerGate.SetResult();
            await cache.DisposeAsync();
            Assert.Equal(0, cache.PendingWrites);
        }
        finally
        {
            writerGate.TrySetResult();
            await cache.ProcessingTask;
            await cache.DisposeAsync();
        }
    }

    [WindowsFact]
    public async Task QueueSaveToCache_PersistsAtomicallyAndCleansTemporaryFile()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, "source.jpg");
        TestImages.WriteJpeg(sourcePath, MagickColors.Red, 400, 200);

        using var bitmap = JpegThumbnailDecoder.Decode(
            sourcePath, 150, CancellationToken.None);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var cache = CreateWriter(catalog);
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

    [WindowsFact]
    public async Task LoadFromCache_LazilyMigratesLegacyPngBytes()
    {
        _fixture.RequireWindows();
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, "source.jpg");
        TestImages.WriteJpeg(sourcePath, MagickColors.Red, 400, 200);

        using var bitmap = JpegThumbnailDecoder.Decode(
            sourcePath, 150, CancellationToken.None);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var cache = CreateWriter(catalog);
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

    [WindowsFact]
    public async Task LoadFromCache_ReturnsUndersizedLegacyEntryForLargeRequest()
    {
        _fixture.RequireWindows();
        var sourcePath = CreateSource();
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var image = new ImageFile(sourcePath) { CatalogId = 1 };
        var cache = CreateWriter(catalog);
        using (var bitmap = JpegThumbnailDecoder.Decode(
            sourcePath, 150, CancellationToken.None))
        {
            cache.QueueSaveToCache(image, bitmap);
        }
        await cache.DisposeAsync();

        await using var reader = new ThumbnailCacheService(catalog);
        using var loaded = reader.LoadFromCache(
            image,
            ThumbnailSizeRequest.For(BrowseThumbnailSize.Large),
            out var dimensions,
            out var satisfiesMinimum);

        Assert.NotNull(loaded);
        Assert.Equal(150, Math.Max(dimensions.Width, dimensions.Height));
        Assert.False(satisfiesMinimum);
    }

    [WindowsFact]
    public async Task LoadFromCache_DecodesPromotedEntryDownToActiveTarget()
    {
        _fixture.RequireWindows();
        var sourcePath = CreateSource(1200, 800);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var image = new ImageFile(sourcePath) { CatalogId = 1 };
        var cache = CreateWriter(catalog);
        using (var bitmap = JpegThumbnailDecoder.Decode(
            sourcePath, 512, CancellationToken.None))
        {
            cache.QueueSaveToCache(image, bitmap);
        }
        await cache.DisposeAsync();

        await using var reader = new ThumbnailCacheService(catalog);
        using var loaded = reader.LoadFromCache(
            image,
            ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium),
            out var dimensions,
            out var satisfiesMinimum);

        Assert.NotNull(loaded);
        Assert.Equal(192, Math.Max(dimensions.Width, dimensions.Height));
        Assert.True(satisfiesMinimum);
    }

    [WindowsFact]
    public async Task QueueSaveToCache_LateSmallWriteCannotReplaceLargeEntry()
    {
        _fixture.RequireWindows();
        var sourcePath = CreateSource(1200, 800);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var image = new ImageFile(sourcePath) { CatalogId = 1 };
        var cache = CreateWriter(catalog);
        using var large = JpegThumbnailDecoder.Decode(
            sourcePath, 512, CancellationToken.None);
        using var small = JpegThumbnailDecoder.Decode(
            sourcePath, 150, CancellationToken.None);

        cache.QueueSaveToCache(image, large);
        cache.QueueSaveToCache(image, small);
        await cache.DisposeAsync();

        Assert.True(JpegDimensions.TryRead(cache.GetCachePath(image), out var dimensions));
        Assert.Equal(512, Math.Max(dimensions.Width, dimensions.Height));
    }

    [Fact]
    public void JpegDimensions_ReadsSofWithoutPixelDecode()
    {
        var sourcePath = CreateSource(640, 360);

        Assert.True(JpegDimensions.TryRead(sourcePath, out var dimensions));
        Assert.Equal(640, dimensions.Width);
        Assert.Equal(360, dimensions.Height);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateSource(int width = 400, int height = 200)
    {
        Directory.CreateDirectory(_tempDirectory);
        var sourcePath = Path.Combine(_tempDirectory, "source.jpg");
        TestImages.WriteJpeg(sourcePath, MagickColors.Red, (uint)width, (uint)height);
        return sourcePath;
    }
}

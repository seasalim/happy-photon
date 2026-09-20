using ImageMagick;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PreviewCacheServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonPreviewCache_{Guid.NewGuid():N}");

    [Fact]
    public void DisplayFloorClippingUsesCachedBgraCodes()
    {
        var clipping = PreviewCacheService.CalculateDisplayFloorClipping(
        [
            0, 1, 0, 255,
            0, 0, 0, 255
        ],
            width: 2,
            height: 1);

        Assert.Equal(new ChannelClip(1, 0.5, 1), clipping.Low);
        Assert.Equal(0.5, clipping.LowAll);
        Assert.Equal(ChannelClip.Empty, clipping.High);
        Assert.Equal(0, clipping.HighAny);
        Assert.False(clipping.IsHighAvailable);
    }

    [Fact]
    public async Task QueueSaveToCache_PersistsJpegAtomically()
    {
        var sourcePath = CreateSource("source.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var recorder = new CullPerfRecorder();
        var cache = new PreviewCacheService(catalog) { CullPerf = recorder };
        var imageFile = new ImageFile(sourcePath) { CatalogId = 1 };
        using var preview = new MagickImage(MagickColors.Blue, 160, 100);

        var outcome = cache.QueueSaveToCache(imageFile, preview, "settings-a", default);
        Assert.True(await outcome.WaitAsync(TestWaits.Condition));
        await cache.DisposeAsync();

        var completed = Assert.Single(recorder.Snapshot());
        Assert.Equal("CacheWriteComplete", completed.Kind);
        Assert.Equal(imageFile.CatalogId, completed.ImageId);
        var cachePath = cache.GetCachePath(imageFile);
        Assert.True(File.Exists(cachePath));
        Assert.True(PreviewCacheMetadata.TryRead(
            cache.GetMetadataPath(imageFile),
            out var metadata));
        Assert.Equal("settings-a", metadata.SettingsHash);
        using var saved = new MagickImage(cachePath);
        Assert.Equal(MagickFormat.Jpeg, saved.Format);
        Assert.Equal(160u, saved.Width);
        Assert.Empty(Directory.GetFiles(
            Path.Combine(catalog.CatalogPath, "assets", "tmp")));
    }

    [Fact]
    public async Task QueueSaveToCache_DropsOldestWhenQueueIsFull()
    {
        var firstSource = CreateSource("first.jpg");
        var secondSource = CreateSource("second.jpg");
        var processingGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var recorder = new CullPerfRecorder();
        var cache = new PreviewCacheService(
            catalog, 1, processingGate.Task, TimeSpan.FromSeconds(5)) { CullPerf = recorder };
        var first = new ImageFile(firstSource) { CatalogId = 1 };
        var second = new ImageFile(secondSource) { CatalogId = 2 };
        using var preview = new MagickImage(MagickColors.Red, 32, 24);

        var firstOutcome = cache.QueueSaveToCache(first, preview, "first-hash", default);
        var secondOutcome = cache.QueueSaveToCache(second, preview, "second-hash", default);
        Assert.False(await firstOutcome.WaitAsync(TestWaits.Condition));
        Assert.False(secondOutcome.IsCompleted);
        processingGate.SetResult();
        await cache.DisposeAsync();

        Assert.True(await secondOutcome.WaitAsync(TestWaits.Condition));
        Assert.False(File.Exists(cache.GetCachePath(first)));
        Assert.False(File.Exists(cache.GetMetadataPath(first)));
        Assert.True(File.Exists(cache.GetCachePath(second)));
        Assert.True(PreviewCacheMetadata.TryRead(
            cache.GetMetadataPath(second),
            out var survivor));
        Assert.Equal("second-hash", survivor.SettingsHash);
        Assert.Collection(recorder.Snapshot(),
            dropped => { Assert.Equal("CacheWriteDropped", dropped.Kind); Assert.Equal(1, dropped.ImageId); },
            complete => { Assert.Equal("CacheWriteComplete", complete.Kind); Assert.Equal(2, complete.ImageId); });
    }

    [Fact]
    public async Task QueueSaveToCache_RejectsResultWhenSourceChanges()
    {
        var sourcePath = CreateSource("source.jpg");
        var processingGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var recorder = new CullPerfRecorder();
        var cache = new PreviewCacheService(
            catalog, 2, processingGate.Task, TimeSpan.FromSeconds(5)) { CullPerf = recorder };
        var imageFile = new ImageFile(sourcePath) { CatalogId = 1 };
        using var preview = new MagickImage(MagickColors.Green, 32, 24);

        var outcome = cache.QueueSaveToCache(imageFile, preview, "settings-a", default);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(1));
        processingGate.SetResult();
        await cache.DisposeAsync();

        Assert.False(await outcome.WaitAsync(TestWaits.Condition));
        Assert.Equal("CacheWriteDropped", Assert.Single(recorder.Snapshot()).Kind);
        Assert.False(File.Exists(cache.GetCachePath(imageFile)));
        Assert.False(File.Exists(cache.GetMetadataPath(imageFile)));
    }

    [Fact]
    public async Task LoadRenderedPreview_ReturnsImageAndSettingsHash()
    {
        var sourcePath = CreateSource("source.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var cache = new PreviewCacheService(catalog);
        var imageFile = new ImageFile(sourcePath) { CatalogId = 1 };
        using var preview = new MagickImage(MagickColors.Purple, 20, 10);

        cache.QueueSaveToCache(imageFile, preview, "settings-a");
        await cache.DisposeAsync();

        using var loaded = cache.LoadRenderedPreview(imageFile);
        Assert.NotNull(loaded);
        Assert.Equal("settings-a", loaded!.SettingsHash);
        Assert.Equal(20u, loaded.Image.Width);
    }

    [Fact]
    public async Task LoadRenderedPreview_ReturnsPersistedRenderIdentity()
    {
        var sourcePath = CreateSource("identity.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "identity"));
        var cache = new PreviewCacheService(catalog);
        var imageFile = new ImageFile(sourcePath) { CatalogId = 1 };
        using var preview = new MagickImage(MagickColors.Purple, 20, 10);
        var identity = new PreviewCacheIdentity(
            new Avalonia.PixelSize(3000, 2000),
            new Avalonia.PixelSize(6000, 4000));

        Assert.True(await cache.QueueSaveToCache(imageFile, preview, "settings-a", identity)
            .WaitAsync(TestWaits.Condition));
        await cache.DisposeAsync();

        using var loaded = cache.LoadRenderedPreview(imageFile);
        Assert.NotNull(loaded);
        Assert.Equal(identity.OriginalViewSize, loaded!.OriginalViewPixelSize);
        Assert.Equal(identity.OriginalImageSize, loaded.OriginalImagePixelSize);
    }

    [Theory]
    [InlineData("disposed")]
    [InlineData("invalid-id")]
    [InlineData("invalid-hash")]
    [InlineData("clone-exception")]
    public async Task QueueSaveToCache_CompletesRejectedWrites(string reason)
    {
        var source = CreateSource("rejected.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        await using var cache = new PreviewCacheService(catalog);
        var image = new ImageFile(source) { CatalogId = reason == "invalid-id" ? 0 : 1 };
        using var pixels = new MagickImage(MagickColors.Blue, 32, 24);
        if (reason == "disposed") await cache.DisposeAsync();
        if (reason == "clone-exception") pixels.Dispose();

        var outcome = cache.QueueSaveToCache(image, pixels,
            reason == "invalid-hash" ? "" : "hash", default);

        Assert.True(outcome.IsCompleted);
        Assert.False(await outcome.WaitAsync(TestWaits.Condition));
        Assert.Equal(0, cache.PendingWrites);
    }

    [Fact]
    public async Task QueueSaveToCache_CompletesChannelRejectionDuringShutdown()
    {
        var source = CreateSource("rejected.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        SettingsHashedCacheWriter? writer = null;
        Task? disposal = null;
        writer = new SettingsHashedCacheWriter(catalog, id =>
        {
            // Close the channel after CanQueue but before TryWrite.
            disposal = writer!.DisposeAsync().AsTask();
            return catalog.GetPreviewPath(id);
        }, 90);
        using var pixels = new MagickImage(MagickColors.Blue, 32, 24);

        var outcome = writer.Queue(new ImageFile(source) { CatalogId = 1 }, pixels, "hash");

        Assert.False(await outcome.WaitAsync(TestWaits.Condition));
        await disposal!.WaitAsync(TestWaits.Condition);
        Assert.Equal(0, writer.PendingWrites);
        Assert.Equal(1, writer.DroppedWrites);
    }

    [Fact]
    public async Task QueueSaveToCache_CompletesSaveExceptionAsDropped()
    {
        var source = CreateSource("exception.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cache = new PreviewCacheService(catalog, 1, release.Task, TimeSpan.Zero);
        var image = new ImageFile(source) { CatalogId = 1 };
        Directory.CreateDirectory(cache.GetCachePath(image));
        using var pixels = new MagickImage(MagickColors.Blue, 32, 24);

        var outcome = cache.QueueSaveToCache(image, pixels, "hash", default);
        release.SetResult();

        Assert.False(await outcome.WaitAsync(TestWaits.Condition));
        Assert.False(File.Exists(cache.GetMetadataPath(image)));
        await TestWaits.UntilAsync(() => cache.PendingWrites == 0);
        Assert.Empty(Directory.GetFiles(catalog.TemporaryAssetsPath));
    }

    [Fact]
    public async Task QueueSaveToCache_CompletesEqualOrLargerEntryAsDropped()
    {
        var source = CreateSource("largest.jpg");
        File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddMinutes(-1));
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        using var gate = new SemaphoreSlim(0);
        await using var writer = new SettingsHashedCacheWriter(catalog,
            catalog.GetRenderedThumbnailPath, 85, versionedDimensionMetadata: true,
            beforeWrite: () => gate.WaitAsync());
        var image = new ImageFile(source) { CatalogId = 1 };
        using var pixels = new MagickImage(MagickColors.Blue, 32, 24);
        var first = writer.Queue(image, pixels, "hash");
        gate.Release();
        Assert.True(await first.WaitAsync(TestWaits.Condition));

        var second = writer.Queue(image, pixels, "hash");
        Assert.False(second.IsCompleted);
        gate.Release();

        Assert.False(await second.WaitAsync(TestWaits.Condition));
        Assert.Equal(1, writer.DroppedWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeAsync_CompletesOutstandingOutcomesWithoutReleasingWriter(bool inHand)
    {
        var source = CreateSource("shutdown.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recorder = new CullPerfRecorder();
        var cache = new PreviewCacheService(catalog, 2,
            inHand ? Task.CompletedTask : release.Task, TimeSpan.Zero,
            writerInHandGate: inHand ? release.Task : null) { CullPerf = recorder };
        using var pixels = new MagickImage(MagickColors.Blue, 32, 24);
        try
        {
            var first = cache.QueueSaveToCache(new ImageFile(source) { CatalogId = 1 },
                pixels, "hash", default);
            if (inHand) await TestWaits.UntilAsync(() => cache.WriterInHandCount == 1);
            var second = cache.QueueSaveToCache(new ImageFile(source) { CatalogId = 2 },
                pixels, "hash", default);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);

            await cache.DisposeAsync().AsTask().WaitAsync(TestWaits.Condition);

            Assert.False(release.Task.IsCompleted);
            Assert.False(cache.ProcessingTask.IsCompleted);
            Assert.True(first.IsCompletedSuccessfully);
            Assert.True(second.IsCompletedSuccessfully);
            Assert.False(await first);
            Assert.False(await second);
            Assert.Equal(2, cache.PendingWrites);
            Assert.All(recorder.Snapshot(), e => Assert.Equal("CacheWriteDropped", e.Kind));
            Assert.Equal(2, recorder.Snapshot().Length);
        }
        finally
        {
            // Release only after verifying bounded shutdown, to clean up owned pixels.
            release.TrySetResult();
            await cache.ProcessingTask.WaitAsync(TestWaits.Condition);
            await cache.DisposeAsync();
        }
        Assert.Equal(0, cache.PendingWrites);
        Assert.Equal(2, recorder.Snapshot().Length);
    }

    private string CreateSource(string name)
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

}

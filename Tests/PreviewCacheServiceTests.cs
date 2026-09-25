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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueueSaveToCache_PersistsJpegAtomically(bool encoded)
    {
        var sourcePath = CreateSource("source.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var recorder = new CullPerfRecorder();
        var cache = new PreviewCacheService(catalog) { CullPerf = recorder };
        var imageFile = new ImageFile(sourcePath) { CatalogId = 1 };
        using var preview = new MagickImage(MagickColors.Blue, 160, 100);

        var bytes = preview.ToByteArray(MagickFormat.Jpeg);
        var outcome = encoded
            ? cache.QueueSaveToCache(imageFile, bytes, "settings-a", default)
            : cache.QueueSaveToCache(imageFile, preview, "settings-a", default);
        Assert.True(await outcome.WaitAsync(TestWaits.Condition));
        await cache.DisposeAsync();

        var completed = Assert.Single(Outcomes(recorder));
        Assert.Equal("CacheWriteComplete", completed.Kind);
        Assert.Equal(imageFile.CatalogId, completed.ImageId);
        Assert.Equal(encoded ? 0 : 1, recorder.Snapshot().Count(e => e.Kind == "CacheEncode"));
        var cachePath = cache.GetCachePath(imageFile);
        if (encoded) Assert.Equal(bytes, File.ReadAllBytes(cachePath));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueueSaveToCache_DropsOldestWhenQueueIsFull(bool encoded)
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

        var firstOutcome = Queue(cache, first, preview, "first-hash", default, encoded);
        var secondOutcome = Queue(cache, second, preview, "second-hash", default, encoded);
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
        Assert.Collection(Outcomes(recorder),
            dropped => { Assert.Equal("CacheWriteDropped", dropped.Kind); Assert.Equal(1, dropped.ImageId); },
            complete => { Assert.Equal("CacheWriteComplete", complete.Kind); Assert.Equal(2, complete.ImageId); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueueSaveToCache_RejectsResultWhenSourceChanges(bool encoded)
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

        var outcome = Queue(cache, imageFile, preview, "settings-a", default, encoded);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(1));
        processingGate.SetResult();
        await cache.DisposeAsync();

        Assert.False(await outcome.WaitAsync(TestWaits.Condition));
        Assert.Equal("CacheWriteDropped", Assert.Single(Outcomes(recorder)).Kind);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadRenderedPreview_ReturnsPersistedRenderIdentity(bool encoded)
    {
        var sourcePath = CreateSource("identity.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "identity"));
        var cache = new PreviewCacheService(catalog);
        var imageFile = new ImageFile(sourcePath) { CatalogId = 1 };
        using var preview = new MagickImage(MagickColors.Purple, 20, 10);
        var identity = new PreviewCacheIdentity(
            new Avalonia.PixelSize(3000, 2000),
            new Avalonia.PixelSize(6000, 4000));

        Assert.True(await Queue(cache, imageFile, preview, "settings-a", identity, encoded)
            .WaitAsync(TestWaits.Condition));
        await cache.DisposeAsync();

        using var loaded = cache.LoadRenderedPreview(imageFile);
        Assert.NotNull(loaded);
        Assert.Equal("settings-a", loaded!.SettingsHash);
        Assert.Equal(identity.OriginalViewSize, loaded.OriginalViewPixelSize);
        Assert.Equal(identity.OriginalImageSize, loaded.OriginalImagePixelSize);
    }

    [Theory]
    [InlineData("disposed", false)]
    [InlineData("disposed", true)]
    [InlineData("invalid-id", false)]
    [InlineData("invalid-id", true)]
    [InlineData("invalid-hash", false)]
    [InlineData("invalid-hash", true)]
    [InlineData("clone-exception", false)]
    public async Task QueueSaveToCache_CompletesRejectedWrites(string reason, bool encoded)
    {
        var source = CreateSource("rejected.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        await using var cache = new PreviewCacheService(catalog);
        var image = new ImageFile(source) { CatalogId = reason == "invalid-id" ? 0 : 1 };
        using var pixels = new MagickImage(MagickColors.Blue, 32, 24);
        if (reason == "disposed") await cache.DisposeAsync();
        if (reason == "clone-exception") pixels.Dispose();

        var outcome = Queue(cache, image, pixels,
            reason == "invalid-hash" ? "" : "hash", default, encoded);

        Assert.True(outcome.IsCompleted);
        Assert.False(await outcome.WaitAsync(TestWaits.Condition));
        Assert.Equal(0, cache.PendingWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueueSaveToCache_CompletesChannelRejectionDuringShutdown(bool encoded)
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

        var image = new ImageFile(source) { CatalogId = 1 };
        var outcome = encoded
            ? writer.Queue(image, pixels.ToByteArray(MagickFormat.Jpeg), "hash", default)
            : writer.Queue(image, pixels, "hash");

        Assert.False(await outcome.WaitAsync(TestWaits.Condition));
        await disposal!.WaitAsync(TestWaits.Condition);
        Assert.Equal(0, writer.PendingWrites);
        Assert.Equal(1, writer.DroppedWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueueSaveToCache_CompletesSaveExceptionAsDropped(bool encoded)
    {
        var source = CreateSource("exception.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var cache = new PreviewCacheService(catalog, 1, release.Task, TimeSpan.Zero);
        var image = new ImageFile(source) { CatalogId = 1 };
        Directory.CreateDirectory(cache.GetCachePath(image));
        using var pixels = new MagickImage(MagickColors.Blue, 32, 24);

        var outcome = Queue(cache, image, pixels, "hash", default, encoded);
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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DisposeAsync_CompletesOutstandingOutcomesWithoutReleasingWriter(bool inHand, bool encoded)
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
            var first = Queue(cache, new ImageFile(source) { CatalogId = 1 },
                pixels, "hash", default, encoded);
            if (inHand) await TestWaits.UntilAsync(() => cache.WriterInHandCount == 1);
            var second = Queue(cache, new ImageFile(source) { CatalogId = 2 },
                pixels, "hash", default, encoded);
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
            Assert.All(Outcomes(recorder), e => Assert.Equal("CacheWriteDropped", e.Kind));
            Assert.Equal(2, Outcomes(recorder).Length);
        }
        finally
        {
            // Release only after verifying bounded shutdown, to clean up owned pixels.
            release.TrySetResult();
            await cache.ProcessingTask.WaitAsync(TestWaits.Condition);
            await cache.DisposeAsync();
        }
        Assert.Equal(0, cache.PendingWrites);
        Assert.Equal(2, Outcomes(recorder).Length);
    }

    [Fact]
    public async Task EncodedWrite_RejectsVersionedDimensionMetadata()
    {
        var source = CreateSource("versioned.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        await using var writer = new SettingsHashedCacheWriter(catalog,
            catalog.GetRenderedThumbnailPath, 85, versionedDimensionMetadata: true);
        using var pixels = new MagickImage(MagickColors.Blue, 32, 24);
        var image = new ImageFile(source) { CatalogId = 1 };

        var outcome = writer.Queue(image, pixels.ToByteArray(MagickFormat.Jpeg), "hash", default);

        Assert.True(outcome.IsCompletedSuccessfully);
        Assert.False(await outcome.WaitAsync(TestWaits.Condition));
        Assert.Equal(0, writer.PendingWrites);
        Assert.Equal(1, writer.DroppedWrites);
        Assert.False(File.Exists(catalog.GetRenderedThumbnailPath(image.CatalogId)));
    }

    private static Task<bool> Queue(PreviewCacheService cache, ImageFile image,
        MagickImage pixels, string hash, PreviewCacheIdentity identity, bool encoded) =>
        encoded
            ? cache.QueueSaveToCache(image, pixels.ToByteArray(MagickFormat.Jpeg), hash, identity)
            : cache.QueueSaveToCache(image, pixels, hash, identity);

    // The writer also records save spans and encodes; these tests observe outcomes.
    private static CullPerfEvent[] Outcomes(CullPerfRecorder recorder) =>
        recorder.Snapshot().Where(e => e.Kind is "CacheWriteComplete" or "CacheWriteDropped").ToArray();

    private string CreateSource(string name)
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        // Cache validity requires a newer timestamp, independent of filesystem resolution.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
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

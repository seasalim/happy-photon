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
        var cache = new PreviewCacheService(catalog);
        var imageFile = new ImageFile(sourcePath) { CatalogId = 1 };
        using var preview = new MagickImage(MagickColors.Blue, 160, 100);

        cache.QueueSaveToCache(imageFile, preview, "settings-a");
        await cache.DisposeAsync();

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
        var cache = new PreviewCacheService(
            catalog, 1, processingGate.Task, TimeSpan.FromSeconds(5));
        var first = new ImageFile(firstSource) { CatalogId = 1 };
        var second = new ImageFile(secondSource) { CatalogId = 2 };
        using var preview = new MagickImage(MagickColors.Red, 32, 24);

        cache.QueueSaveToCache(first, preview, "first-hash");
        cache.QueueSaveToCache(second, preview, "second-hash");
        processingGate.SetResult();
        await cache.DisposeAsync();

        Assert.False(File.Exists(cache.GetCachePath(first)));
        Assert.False(File.Exists(cache.GetMetadataPath(first)));
        Assert.True(File.Exists(cache.GetCachePath(second)));
        Assert.True(PreviewCacheMetadata.TryRead(
            cache.GetMetadataPath(second),
            out var survivor));
        Assert.Equal("second-hash", survivor.SettingsHash);
    }

    [Fact]
    public async Task QueueSaveToCache_RejectsResultWhenSourceChanges()
    {
        var sourcePath = CreateSource("source.jpg");
        var processingGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var cache = new PreviewCacheService(
            catalog, 2, processingGate.Task, TimeSpan.FromSeconds(5));
        var imageFile = new ImageFile(sourcePath) { CatalogId = 1 };
        using var preview = new MagickImage(MagickColors.Green, 32, 24);

        cache.QueueSaveToCache(imageFile, preview, "settings-a");
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(1));
        processingGate.SetResult();
        await cache.DisposeAsync();

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

        cache.QueueSaveToCache(imageFile, preview, "settings-a", identity);
        await cache.DisposeAsync();

        using var loaded = cache.LoadRenderedPreview(imageFile);
        Assert.NotNull(loaded);
        Assert.Equal(identity.OriginalViewSize, loaded!.OriginalViewPixelSize);
        Assert.Equal(identity.OriginalImageSize, loaded.OriginalImagePixelSize);
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

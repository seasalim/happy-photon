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
    public async Task QueueSaveToCache_PersistsJpegAtomically()
    {
        var sourcePath = CreateSource("source.jpg");
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var cache = new PreviewCacheService(catalog);
        var imageFile = new ImageFile(sourcePath) { CatalogId = 1 };
        using var preview = new MagickImage(MagickColors.Blue, 160, 100);

        cache.QueueSaveToCache(imageFile, preview);
        await cache.DisposeAsync();

        var cachePath = cache.GetCachePath(imageFile);
        Assert.True(File.Exists(cachePath));
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

        cache.QueueSaveToCache(first, preview);
        cache.QueueSaveToCache(second, preview);
        processingGate.SetResult();
        await cache.DisposeAsync();

        Assert.False(File.Exists(cache.GetCachePath(first)));
        Assert.True(File.Exists(cache.GetCachePath(second)));
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

        cache.QueueSaveToCache(imageFile, preview);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(1));
        processingGate.SetResult();
        await cache.DisposeAsync();

        Assert.False(File.Exists(cache.GetCachePath(imageFile)));
    }

    [Fact]
    public async Task BlockedWriter_DoesNotBlockPreviewEdits()
    {
        var sourcePath = CreateSource("source.dng");
        var processingGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var catalog = new CatalogService(Path.Combine(_tempDirectory, "catalog"));
        var cache = new PreviewCacheService(
            catalog, 2, processingGate.Task, TimeSpan.FromSeconds(5));
        var editService = new EditApplicationService();
        var service = new PreviewService(
            catalog,
            new StubRawProcessingService(),
            editService,
            new HistogramService(),
            cache);
        var imageFile = new ImageFile(sourcePath) { CatalogId = 1 };

        var first = await service.ApplyEditsToPreviewAsync(
            imageFile, new EditSettings(), skipHistogram: true)
            .WaitAsync(TimeSpan.FromSeconds(5));
        first.preview?.Dispose();
        var second = await service.ApplyEditsToPreviewAsync(
            imageFile, new EditSettings { Exposure = 1 }, skipHistogram: true)
            .WaitAsync(TimeSpan.FromSeconds(5));
        second.preview?.Dispose();

        processingGate.SetResult();
        await service.DisposeAsync();
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

    private sealed class StubRawProcessingService : IRawProcessingService
    {
        public bool IsAvailable => true;
        public MagickImage? DecodeHalfSize(string filePath) =>
            new(MagickColors.Black, 64, 48);
        public byte[]? ExtractThumbnail(string filePath) => null;
        public MagickImage? DecodeFull(string filePath) => null;
        public RawMetadata? ExtractMetadata(string filePath) => null;
    }
}

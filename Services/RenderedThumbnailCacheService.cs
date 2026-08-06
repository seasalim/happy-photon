using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class RenderedThumbnailCacheService : IAsyncDisposable
{
    private readonly CatalogService _catalogService;
    private readonly SettingsHashedCacheWriter _writer;
    private readonly ConcurrentDictionary<long, CachedHash> _hashes = new();

    public RenderedThumbnailCacheService(CatalogService catalogService)
    {
        _catalogService = catalogService;
        _writer = new SettingsHashedCacheWriter(
            catalogService,
            catalogService.GetRenderedThumbnailPath,
            85);
    }

    internal RenderedThumbnailCacheService(
        CatalogService catalogService,
        int queueCapacity,
        Task processingGate,
        TimeSpan shutdownDrainTimeout)
    {
        _catalogService = catalogService;
        _writer = new SettingsHashedCacheWriter(
            catalogService,
            catalogService.GetRenderedThumbnailPath,
            85,
            queueCapacity,
            processingGate,
            shutdownDrainTimeout);
    }

    public Bitmap? LoadMatching(ImageFile imageFile, string settingsHash)
    {
        if (imageFile.CatalogId == 0 || string.IsNullOrWhiteSpace(settingsHash))
            return null;

        try
        {
            var path = _catalogService.GetRenderedThumbnailPath(imageFile.CatalogId);
            var metadataPath = Path.ChangeExtension(path, ".meta");
            var writeTime = File.GetLastWriteTimeUtc(path);
            var metadataWriteTime = File.GetLastWriteTimeUtc(metadataPath);
            if (writeTime <= File.GetLastWriteTimeUtc(imageFile.FilePath))
            {
                return null;
            }

            if (!_hashes.TryGetValue(imageFile.CatalogId, out var cached) ||
                cached.WriteTime != writeTime ||
                cached.MetadataWriteTime != metadataWriteTime)
            {
                cached = new CachedHash(
                    writeTime,
                    metadataWriteTime,
                    File.ReadAllText(metadataPath).Trim());
                _hashes[imageFile.CatalogId] = cached;
            }
            if (!string.Equals(
                cached.SettingsHash,
                settingsHash,
                StringComparison.Ordinal)) return null;

            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    public bool HasCacheEntry(ImageFile imageFile)
    {
        if (imageFile.CatalogId == 0) return false;
        var path = _catalogService.GetRenderedThumbnailPath(imageFile.CatalogId);
        return File.Exists(path) || File.Exists(Path.ChangeExtension(path, ".meta"));
    }

    public void QueueSaveToCache(
        ImageFile imageFile,
        Bitmap bitmap,
        string settingsHash) =>
        _writer.Queue(imageFile, bitmap, settingsHash);

    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    internal Task ProcessingTask => _writer.ProcessingTask;

    private sealed record CachedHash(
        DateTime WriteTime,
        DateTime MetadataWriteTime,
        string SettingsHash);
}

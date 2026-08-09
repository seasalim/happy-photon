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
            85,
            versionedDimensionMetadata: true);
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
            shutdownDrainTimeout,
            versionedDimensionMetadata: true);
    }

    public Bitmap? LoadMatching(ImageFile imageFile, string settingsHash)
        => LoadMatching(
            imageFile,
            settingsHash,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            out _);

    public Bitmap? LoadMatching(
        ImageFile imageFile,
        string settingsHash,
        ThumbnailSizeRequest request,
        out bool satisfiesMinimum)
    {
        satisfiesMinimum = false;
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
                if (!RenderedThumbnailMetadata.TryRead(
                    metadataPath,
                    path,
                    out var metadata))
                {
                    return null;
                }
                cached = new CachedHash(
                    writeTime,
                    metadataWriteTime,
                    metadata);
                _hashes[imageFile.CatalogId] = cached;
            }
            if (!string.Equals(
                cached.Metadata.SettingsHash,
                settingsHash,
                StringComparison.Ordinal)) return null;

            var bitmap = DecodeForRequest(path, cached.Metadata, request);
            satisfiesMinimum = Math.Max(
                bitmap.PixelSize.Width,
                bitmap.PixelSize.Height) >= request.MinimumDimension;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap DecodeForRequest(
        string path,
        RenderedThumbnailMetadata metadata,
        ThumbnailSizeRequest request)
    {
        if (metadata.LongEdge <= request.GenerationDimension)
        {
            return new Bitmap(path);
        }

        using var stream = File.OpenRead(path);
        return metadata.PixelWidth >= metadata.PixelHeight
            ? Bitmap.DecodeToWidth(stream, request.GenerationDimension)
            : Bitmap.DecodeToHeight(stream, request.GenerationDimension);
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
        RenderedThumbnailMetadata Metadata);
}

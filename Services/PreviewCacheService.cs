using Avalonia.Media.Imaging;
using ImageMagick;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class PreviewCacheService : IAsyncDisposable
{
    private readonly CatalogService _catalogService;
    private readonly SettingsHashedCacheWriter _writer;

    public PreviewCacheService(CatalogService catalogService) : this(
        catalogService,
        8,
        Task.CompletedTask,
        TimeSpan.FromSeconds(2))
    {
    }

    internal PreviewCacheService(
        CatalogService catalogService,
        int queueCapacity,
        Task processingGate,
        TimeSpan shutdownDrainTimeout,
        Task? writerInHandGate = null)
    {
        _catalogService = catalogService;
        _writer = new SettingsHashedCacheWriter(
            catalogService,
            catalogService.GetPreviewPath,
            90,
            queueCapacity,
            processingGate,
            shutdownDrainTimeout,
            writerInHandGate: writerInHandGate);
    }

    public string GetCachePath(ImageFile imageFile)
    {
        if (imageFile.CatalogId == 0)
            throw new InvalidOperationException("Image must be in catalog before caching.");
        return _catalogService.GetPreviewPath(imageFile.CatalogId);
    }

    public string GetMetadataPath(ImageFile imageFile) =>
        Path.ChangeExtension(GetCachePath(imageFile), ".meta");

    public bool IsCacheValid(ImageFile imageFile)
    {
        if (imageFile.CatalogId == 0) return false;
        var path = _catalogService.GetPreviewPath(imageFile.CatalogId);
        if (!File.Exists(path)) return false;
        try
        {
            return File.GetLastWriteTimeUtc(path) >
                File.GetLastWriteTimeUtc(imageFile.FilePath);
        }
        catch
        {
            return false;
        }
    }

    public CachedPreview? LoadRenderedPreview(ImageFile imageFile)
    {
        if (!IsCacheValid(imageFile)) return null;
        try
        {
            var path = _catalogService.GetPreviewPath(imageFile.CatalogId);
            var metadataPath = Path.ChangeExtension(path, ".meta");
            PreviewCacheMetadata metadata = default;
            var hasMetadata = File.Exists(metadataPath) &&
                PreviewCacheMetadata.TryRead(metadataPath, out metadata);
            return new CachedPreview(
                new MagickImage(path),
                hasMetadata ? metadata.SettingsHash : null,
                hasMetadata ? metadata.Identity?.OriginalViewSize : null,
                hasMetadata ? metadata.Identity?.OriginalImageSize : null);
        }
        catch
        {
            return null;
        }
    }

    internal bool HasSettingsMatchedEntry(
        ImageFile imageFile,
        string settingsHash,
        DateTime? sourceWriteTime = null)
    {
        if (!IsCacheValid(imageFile)) return false;
        try
        {
            if (sourceWriteTime.HasValue &&
                File.GetLastWriteTimeUtc(imageFile.FilePath) != sourceWriteTime.Value)
                return false;
            return PreviewCacheMetadata.TryRead(
                    GetMetadataPath(imageFile),
                    out var metadata) &&
                string.Equals(
                    metadata.SettingsHash,
                    settingsHash,
                    StringComparison.Ordinal);
        }
        catch { return false; }
    }

    public void QueueSaveToCache(
        ImageFile imageFile,
        MagickImage image,
        string settingsHash) =>
        _writer.Queue(imageFile, image, settingsHash);

    internal void QueueSaveToCache(
        ImageFile imageFile,
        MagickImage image,
        string settingsHash,
        PreviewCacheIdentity identity) =>
        _writer.Queue(imageFile, image, settingsHash, identity);

    public void QueueSaveToCache(
        ImageFile imageFile,
        Bitmap bitmap,
        string settingsHash) =>
        _writer.Queue(imageFile, bitmap, settingsHash);

    internal void QueueSaveToCache(
        ImageFile imageFile,
        Bitmap bitmap,
        string settingsHash,
        PreviewCacheIdentity identity) =>
        _writer.Queue(imageFile, bitmap, settingsHash, identity);

    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    public int PendingWrites => _writer.PendingWrites;
    internal int WriterInHandCount => _writer.WriterInHandCount;

    internal Task ProcessingTask => _writer.ProcessingTask;

    internal static ClippingStats CalculateDisplayFloorClipping(
        byte[] bgra,
        int width,
        int height)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        var pixelCount = checked(width * height);
        if (bgra.Length != checked(pixelCount * 4))
        {
            throw new ArgumentException(
                "The BGRA buffer length must match its dimensions.",
                nameof(bgra));
        }

        long lowR = 0, lowG = 0, lowB = 0, lowAll = 0;
        for (var offset = 0; offset < bgra.Length; offset += 4)
        {
            var bLow = bgra[offset] == 0;
            var gLow = bgra[offset + 1] == 0;
            var rLow = bgra[offset + 2] == 0;
            if (rLow) lowR++;
            if (gLow) lowG++;
            if (bLow) lowB++;
            if (rLow && gLow && bLow) lowAll++;
        }

        return new ClippingStats(
            ChannelClip.Empty,
            new ChannelClip(
                lowR / (double)pixelCount,
                lowG / (double)pixelCount,
                lowB / (double)pixelCount),
            HighAny: 0,
            lowAll / (double)pixelCount,
            IsHighAvailable: false);
    }
}

public sealed record CachedPreview(
    MagickImage Image,
    string? SettingsHash,
    Avalonia.PixelSize? OriginalViewPixelSize = null,
    Avalonia.PixelSize? OriginalImagePixelSize = null) : IDisposable
{
    public void Dispose() => Image.Dispose();
}

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
        TimeSpan shutdownDrainTimeout)
    {
        _catalogService = catalogService;
        _writer = new SettingsHashedCacheWriter(
            catalogService,
            catalogService.GetPreviewPath,
            90,
            queueCapacity,
            processingGate,
            shutdownDrainTimeout);
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
            var hash = File.Exists(metadataPath)
                ? File.ReadAllText(metadataPath).Trim()
                : null;
            return new CachedPreview(
                new MagickImage(path),
                string.IsNullOrWhiteSpace(hash) ? null : hash);
        }
        catch
        {
            return null;
        }
    }

    public void QueueSaveToCache(
        ImageFile imageFile,
        MagickImage image,
        string settingsHash) =>
        _writer.Queue(imageFile, image, settingsHash);

    public void QueueSaveToCache(
        ImageFile imageFile,
        Bitmap bitmap,
        string settingsHash) =>
        _writer.Queue(imageFile, bitmap, settingsHash);

    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    internal Task ProcessingTask => _writer.ProcessingTask;
}

public sealed record CachedPreview(
    MagickImage Image,
    string? SettingsHash) : IDisposable
{
    public void Dispose() => Image.Dispose();
}

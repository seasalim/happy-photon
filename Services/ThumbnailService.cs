using System.Diagnostics;
using Avalonia.Media.Imaging;
using ImageMagick;
using HappyPhoton.Models;
using static HappyPhoton.Services.BitmapConversionService;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

public class ThumbnailService : IAsyncDisposable
{
    private const int ThumbnailSize = 150;

    private readonly CatalogService _catalogService;
    private readonly ThumbnailCacheService _thumbnailCache;
    private readonly RenderedThumbnailCacheService _renderedThumbnailCache;
    private readonly EmbeddedPreviewExtractor _embeddedPreviewExtractor;
    private readonly ThumbnailRenderer _renderer;

    internal ThumbnailService(
        CatalogService catalogService,
        IRawProcessingService rawService,
        RenderPipeline renderPipeline,
        RenderedThumbnailCacheService renderedThumbnailCache)
    {
        _catalogService = catalogService;
        _thumbnailCache = new ThumbnailCacheService(catalogService);
        _renderedThumbnailCache = renderedThumbnailCache;
        _embeddedPreviewExtractor = new EmbeddedPreviewExtractor(
            rawService,
            ThumbnailSize);
        _renderer = new ThumbnailRenderer(renderPipeline, ThumbnailSize);
    }

    public async Task<Bitmap?> LoadThumbnailAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken = default)
    {
        var settings = imageFile.EditSettings.Clone();
        if (imageFile.IsRaw && settings.HasEdits)
        {
            await imageFile.EnsureCatalogIdAsync(_catalogService);
            var rendered = await Task.Run(
                () => _renderedThumbnailCache.LoadMatching(
                    imageFile,
                    RenderSettingsHash.Compute(settings)),
                cancellationToken);
            if (rendered != null) return rendered;
        }

        var source = await LoadUneditedThumbnailAsync(imageFile, cancellationToken);
        if (source == null || !settings.HasEdits) return source;

        return await Task.Run(() => ApplyFallback(
            imageFile,
            source,
            settings,
            cancellationToken));
    }

    private Bitmap? ApplyFallback(
        ImageFile imageFile,
        Bitmap source,
        EditSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rendered = imageFile.IsRaw
                ? _renderer.RenderRawGeometry(source, settings)
                : _renderer.RenderStandardEdits(source, settings);
            source.Dispose();
            return rendered;
        }
        catch (OperationCanceledException)
        {
            source.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            LogDebug(
                nameof(LoadThumbnailAsync),
                $"Failed to apply thumbnail edits: {ex.Message}",
                imageFile.FilePath);
            return source;
        }
    }

    public async Task<Bitmap?> LoadUneditedThumbnailAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken = default)
    {
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        return await Task.Run(
            () => LoadUnedited(imageFile, cancellationToken),
            cancellationToken);
    }

    private Bitmap? LoadUnedited(
        ImageFile imageFile,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_thumbnailCache.IsCacheValid(imageFile))
            {
                var cached = _thumbnailCache.LoadFromCache(imageFile);
                if (cached != null)
                {
                    LogPerformance(
                        nameof(LoadThumbnailAsync),
                        "CacheHit",
                        stopwatch.ElapsedMilliseconds,
                        imageFile.FilePath);
                    return cached;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            Bitmap? bitmap = imageFile.IsRaw
                ? _embeddedPreviewExtractor.TryExtract(
                    imageFile.FilePath,
                    cancellationToken)
                : null;
            bitmap ??= GenerateThumbnailFromFullImage(
                imageFile.FilePath,
                cancellationToken);
            if (bitmap != null && !cancellationToken.IsCancellationRequested)
            {
                _thumbnailCache.QueueSaveToCache(imageFile, bitmap);
            }
            return bitmap;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            LogDebug(
                nameof(LoadThumbnailAsync),
                $"Failed: {ex.Message}",
                imageFile.FilePath);
            return null;
        }
    }

    private static Bitmap? GenerateThumbnailFromFullImage(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var extension = Path.GetExtension(filePath).ToUpperInvariant();
            if (extension is ".JPG" or ".JPEG")
            {
                try
                {
                    return JpegThumbnailDecoder.Decode(
                        filePath,
                        ThumbnailSize,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                }
            }

            var settings = new MagickReadSettings();
            if (extension is ".JPG" or ".JPEG")
            {
                ApplyJpegSizeHint(settings, ThumbnailSize * 3);
            }
            using var image = new MagickImage(filePath, settings);
            cancellationToken.ThrowIfCancellationRequested();
            image.AutoOrient();
            ApplyThumbnailSize(image, ThumbnailSize);
            cancellationToken.ThrowIfCancellationRequested();
            return ConvertToBitmap(image);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            HandleImageLoadError(ex, filePath);
            return null;
        }
    }

    public bool IsCacheValid(ImageFile imageFile) =>
        _thumbnailCache.IsCacheValid(imageFile);

    public bool HasRenderedCacheEntry(ImageFile imageFile) =>
        _renderedThumbnailCache.HasCacheEntry(imageFile);

    public async ValueTask DisposeAsync()
    {
        await _thumbnailCache.DisposeAsync();
    }
}

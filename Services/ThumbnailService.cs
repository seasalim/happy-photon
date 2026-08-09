using System.Diagnostics;
using Avalonia.Media.Imaging;
using ImageMagick;
using HappyPhoton.Models;
using static HappyPhoton.Services.BitmapConversionService;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

public class ThumbnailService : IAsyncDisposable
{
    internal const int ThumbnailSize = 150;

    private readonly CatalogService _catalogService;
    private readonly ThumbnailCacheService _thumbnailCache;
    private readonly RenderedThumbnailCacheService _renderedThumbnailCache;
    private readonly EmbeddedPreviewExtractor _embeddedPreviewExtractor;
    private readonly ThumbnailRenderer _renderer;
    private readonly ISourceAvailabilityService _availabilityService;
    private readonly Func<ImageFile, CancellationToken, Bitmap?> _loadSource;

    internal ThumbnailService(
        CatalogService catalogService,
        IRawProcessingService rawService,
        RenderPipeline renderPipeline,
        RenderedThumbnailCacheService renderedThumbnailCache,
        ISourceAvailabilityService? availabilityService = null,
        Func<ImageFile, CancellationToken, Bitmap?>? loadSource = null)
    {
        _catalogService = catalogService;
        _thumbnailCache = new ThumbnailCacheService(catalogService);
        _renderedThumbnailCache = renderedThumbnailCache;
        _embeddedPreviewExtractor = new EmbeddedPreviewExtractor(
            rawService,
            ThumbnailSize);
        _renderer = new ThumbnailRenderer(renderPipeline, ThumbnailSize);
        _availabilityService = availabilityService ??
            new SourceAvailabilityService();
        _loadSource = loadSource ?? LoadSource;
    }

    public async Task<ThumbnailLoadResult> LoadThumbnailAsync(
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
            if (rendered != null) return ThumbnailLoadResult.Loaded(rendered);
        }

        var source = await LoadUneditedThumbnailAsync(imageFile, cancellationToken);
        if (source.Status != ThumbnailLoadStatus.Loaded || !settings.HasEdits)
        {
            return source;
        }

        using (source)
        {
            var bitmap = source.DetachBitmap()!;
            var rendered = await Task.Run(() => ApplyFallback(
                imageFile,
                bitmap,
                settings,
                cancellationToken));
            return rendered != null
                ? ThumbnailLoadResult.Loaded(rendered)
                : ThumbnailLoadResult.Failed();
        }
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
            throw;
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

    public Task<ThumbnailLoadResult> LoadUneditedThumbnailAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken = default) =>
        LoadUneditedThumbnailAsync(
            imageFile,
            SourceReadIntent.Background,
            cancellationToken);

    internal async Task<ThumbnailLoadResult> LoadUneditedThumbnailAsync(
        ImageFile imageFile,
        SourceReadIntent intent,
        CancellationToken cancellationToken = default)
    {
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        return await Task.Run(
            () => LoadUnedited(imageFile, intent, cancellationToken),
            cancellationToken);
    }

    private ThumbnailLoadResult LoadUnedited(
        ImageFile imageFile,
        SourceReadIntent intent,
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
                    return ThumbnailLoadResult.Loaded(cached);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var availability = _availabilityService.GetAvailability(
                imageFile.FilePath);
            if (!SourceAccessPolicy.CanRead(availability, intent))
            {
                return availability == SourceAvailability.RequiresHydration
                    ? ThumbnailLoadResult.Deferred()
                    : ThumbnailLoadResult.Failed();
            }

            var bitmap = _loadSource(imageFile, cancellationToken);
            if (bitmap != null && !cancellationToken.IsCancellationRequested)
            {
                _thumbnailCache.QueueSaveToCache(imageFile, bitmap);
            }
            return bitmap != null
                ? ThumbnailLoadResult.Loaded(bitmap)
                : ThumbnailLoadResult.Failed();
        }
        catch (OperationCanceledException)
        {
            return ThumbnailLoadResult.Failed();
        }
        catch (Exception ex)
        {
            LogDebug(
                nameof(LoadThumbnailAsync),
                $"Failed: {ex.Message}",
                imageFile.FilePath);
            return ThumbnailLoadResult.Failed();
        }
    }

    private Bitmap? LoadSource(
        ImageFile imageFile,
        CancellationToken cancellationToken)
    {
        Bitmap? bitmap = imageFile.IsRaw
            ? _embeddedPreviewExtractor.TryExtract(
                imageFile.FilePath,
                cancellationToken)
            : null;
        return bitmap ?? GenerateThumbnailFromFullImage(
            imageFile.FilePath,
            cancellationToken);
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

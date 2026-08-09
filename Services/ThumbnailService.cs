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
    private readonly Func<ImageFile, int, CancellationToken, Bitmap?> _loadSource;

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
        _embeddedPreviewExtractor = new EmbeddedPreviewExtractor(rawService);
        _renderer = new ThumbnailRenderer(renderPipeline);
        _availabilityService = availabilityService ??
            new SourceAvailabilityService();
        _loadSource = loadSource == null
            ? LoadSource
            : (image, _, token) => loadSource(image, token);
    }

    public async Task<ThumbnailLoadResult> LoadThumbnailAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken = default)
        => await LoadThumbnailAsync(
            imageFile,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            cancellationToken);

    public async Task<ThumbnailLoadResult> LoadThumbnailAsync(
        ImageFile imageFile,
        ThumbnailSizeRequest request,
        CancellationToken cancellationToken = default)
        => await LoadThumbnailAsync(
            imageFile,
            request,
            allowUndersizedCachePlaceholder: true,
            cancellationToken);

    internal async Task<ThumbnailLoadResult> LoadThumbnailAsync(
        ImageFile imageFile,
        ThumbnailSizeRequest request,
        bool allowUndersizedCachePlaceholder,
        CancellationToken cancellationToken = default)
    {
        var settings = imageFile.EditSettings.Clone();
        if (imageFile.IsRaw && settings.HasEdits)
        {
            await imageFile.EnsureCatalogIdAsync(_catalogService);
            var rendered = await Task.Run(
                () => _renderedThumbnailCache.LoadMatching(
                    imageFile,
                    RenderSettingsHash.Compute(settings),
                    request,
                    out _),
                cancellationToken);
            if (rendered != null)
            {
                return ThumbnailLoadResult.Loaded(
                    rendered,
                    request,
                    sourceCannotProvideRequestedQuality:
                        Math.Max(rendered.PixelSize.Width, rendered.PixelSize.Height) <
                        request.MinimumDimension);
            }
        }

        var source = await LoadUneditedThumbnailAsync(
            imageFile,
            request,
            allowUndersizedCachePlaceholder,
            SourceReadIntent.Background,
            cancellationToken);
        if (source.Status != ThumbnailLoadStatus.Loaded || !settings.HasEdits)
        {
            return source;
        }

        using (source)
        {
            var bitmap = source.DetachBitmap()!;
            var sourceSatisfiedMinimum = source.SatisfiesMinimumDimension;
            var betterResultDeferred = source.BetterResultDeferredForHydration;
            var cannotProvideQuality = source.SourceCannotProvideRequestedQuality;
            var rendered = await Task.Run(() => ApplyFallback(
                imageFile,
                bitmap,
                settings,
                request.GenerationDimension,
                cancellationToken));
            return rendered != null
                ? ThumbnailLoadResult.Loaded(
                    rendered,
                    request,
                    betterResultDeferred,
                    cannotProvideQuality ||
                        sourceSatisfiedMinimum &&
                        Math.Max(
                            rendered.PixelSize.Width,
                            rendered.PixelSize.Height) < request.MinimumDimension)
                : ThumbnailLoadResult.Failed(request);
        }
    }

    private Bitmap? ApplyFallback(
        ImageFile imageFile,
        Bitmap source,
        EditSettings settings,
        int generationDimension,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rendered = imageFile.IsRaw
                ? _renderer.RenderRawGeometry(source, settings, generationDimension)
                : _renderer.RenderStandardEdits(source, settings, generationDimension);
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
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            allowUndersizedCachePlaceholder: true,
            SourceReadIntent.Background,
            cancellationToken);

    public Task<ThumbnailLoadResult> LoadUneditedThumbnailAsync(
        ImageFile imageFile,
        ThumbnailSizeRequest request,
        CancellationToken cancellationToken = default) =>
        LoadUneditedThumbnailAsync(
            imageFile,
            request,
            allowUndersizedCachePlaceholder: true,
            SourceReadIntent.Background,
            cancellationToken);

    internal async Task<ThumbnailLoadResult> LoadUneditedThumbnailAsync(
        ImageFile imageFile,
        ThumbnailSizeRequest request,
        bool allowUndersizedCachePlaceholder,
        SourceReadIntent intent,
        CancellationToken cancellationToken = default)
    {
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        return await Task.Run(
            () => LoadUnedited(
                imageFile,
                request,
                allowUndersizedCachePlaceholder,
                intent,
                cancellationToken),
            cancellationToken);
    }

    private ThumbnailLoadResult LoadUnedited(
        ImageFile imageFile,
        ThumbnailSizeRequest request,
        bool allowUndersizedCachePlaceholder,
        SourceReadIntent intent,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_thumbnailCache.IsCacheValid(imageFile))
            {
                var cached = _thumbnailCache.LoadFromCache(
                    imageFile,
                    request,
                    out _,
                    out var satisfiesMinimum);
                if (cached != null)
                {
                    LogPerformance(
                        nameof(LoadThumbnailAsync),
                        "CacheHit",
                        stopwatch.ElapsedMilliseconds,
                        imageFile.FilePath);
                    if (satisfiesMinimum)
                    {
                        return ThumbnailLoadResult.Loaded(cached, request);
                    }

                    var cachedAvailability = _availabilityService.GetAvailability(
                        imageFile.FilePath);
                    if (!SourceAccessPolicy.CanRead(cachedAvailability, intent))
                    {
                        return ThumbnailLoadResult.Loaded(
                            cached,
                            request,
                            betterResultDeferredForHydration: true);
                    }

                    if (allowUndersizedCachePlaceholder)
                    {
                        return ThumbnailLoadResult.Loaded(cached, request);
                    }

                    cached.Dispose();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var availability = _availabilityService.GetAvailability(
                imageFile.FilePath);
            if (!SourceAccessPolicy.CanRead(availability, intent))
            {
                return availability == SourceAvailability.RequiresHydration
                    ? ThumbnailLoadResult.Deferred(request)
                    : ThumbnailLoadResult.Failed(request);
            }

            var bitmap = _loadSource(
                imageFile,
                request.GenerationDimension,
                cancellationToken);
            if (bitmap != null && !cancellationToken.IsCancellationRequested)
            {
                _thumbnailCache.QueueSaveToCache(imageFile, bitmap);
            }
            return bitmap != null
                ? ThumbnailLoadResult.Loaded(
                    bitmap,
                    request,
                    sourceCannotProvideRequestedQuality:
                        Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height) <
                        request.MinimumDimension)
                : ThumbnailLoadResult.Failed(request);
        }
        catch (OperationCanceledException)
        {
            return ThumbnailLoadResult.Failed(request);
        }
        catch (Exception ex)
        {
            LogDebug(
                nameof(LoadThumbnailAsync),
                $"Failed: {ex.Message}",
                imageFile.FilePath);
            return ThumbnailLoadResult.Failed(request);
        }
    }

    private Bitmap? LoadSource(
        ImageFile imageFile,
        int generationDimension,
        CancellationToken cancellationToken)
    {
        Bitmap? bitmap = imageFile.IsRaw
            ? _embeddedPreviewExtractor.TryExtract(
                imageFile.FilePath,
                generationDimension,
                cancellationToken)
            : null;
        return bitmap ?? GenerateThumbnailFromFullImage(
            imageFile.FilePath,
            generationDimension,
            cancellationToken);
    }

    private static Bitmap? GenerateThumbnailFromFullImage(
        string filePath,
        int generationDimension,
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
                        generationDimension,
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
                ApplyJpegSizeHint(settings, generationDimension);
            }
            using var image = new MagickImage(filePath, settings);
            cancellationToken.ThrowIfCancellationRequested();
            image.AutoOrient();
            ApplyThumbnailSize(image, generationDimension);
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

    public bool IsCacheValid(
        ImageFile imageFile,
        ThumbnailSizeRequest request) =>
        _thumbnailCache.IsCacheValid(imageFile, request);

    public bool HasRenderedCacheEntry(ImageFile imageFile) =>
        _renderedThumbnailCache.HasCacheEntry(imageFile);

    public async ValueTask DisposeAsync()
    {
        await _thumbnailCache.DisposeAsync();
    }
}

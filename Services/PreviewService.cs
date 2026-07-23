using System.Diagnostics;
using Avalonia.Media.Imaging;
using ImageMagick;
using HappyPhoton.Models;
using static HappyPhoton.Services.ImageServiceHelpers;
using static HappyPhoton.Services.BitmapConversionService;

namespace HappyPhoton.Services;

/// <summary>
/// Service for loading and caching image previews with fast edit application.
/// Maintains a cached preview image for responsive slider updates.
/// </summary>
public class PreviewService : IAsyncDisposable
{
    private const int PreviewMaxDimension = 1600;

    private readonly ICatalogService _catalogService;
    private readonly PreviewCacheService _previewCache;
    private readonly IRawProcessingService _rawService;
    private readonly EditApplicationService _editService;
    private readonly HistogramService _histogramService;

    private MagickImage? _cachedPreviewImage;
    private string? _cachedPreviewPath;
    private readonly SemaphoreSlim _previewCacheGate = new(1, 1);

    public PreviewService(
        ICatalogService catalogService,
        IRawProcessingService rawService,
        EditApplicationService editService,
        HistogramService histogramService) : this(
            catalogService,
            rawService,
            editService,
            histogramService,
            new PreviewCacheService(catalogService))
    {
    }

    internal PreviewService(
        ICatalogService catalogService,
        IRawProcessingService rawService,
        EditApplicationService editService,
        HistogramService histogramService,
        PreviewCacheService previewCache)
    {
        _catalogService = catalogService;
        _previewCache = previewCache;
        _rawService = rawService;
        _editService = editService;
        _histogramService = histogramService;
    }

    public async Task<(Bitmap? preview, HistogramData histogram)> LoadPreviewWithHistogramAsync(
        ImageFile imageFile, EditSettings settings, bool skipHistogram = false, CancellationToken cancellationToken = default)
    {
        await imageFile.EnsureCatalogIdAsync(_catalogService);

        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var swTotal = Stopwatch.StartNew();
            var histogram = new HistogramData();

            LogDebug(nameof(LoadPreviewWithHistogramAsync), "Entry", imageFile.FilePath);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                MagickImage baseImage;
                _previewCacheGate.Wait(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _cachedPreviewImage?.Dispose();
                    _cachedPreviewImage = null;
                    _cachedPreviewPath = null;

                    MagickImage? loadedImage = null;
                    try
                    {
                        loadedImage = LoadPreviewImage(imageFile);
                        cancellationToken.ThrowIfCancellationRequested();
                        _cachedPreviewImage = loadedImage;
                        _cachedPreviewPath = imageFile.FilePath;
                        loadedImage = null;
                    }
                    finally
                    {
                        loadedImage?.Dispose();
                    }

                    baseImage = new MagickImage(_cachedPreviewImage);
                }
                finally
                {
                    _previewCacheGate.Release();
                }

                using (baseImage)
                {
                    LogDebug(nameof(LoadPreviewWithHistogramAsync), $"Preview loaded: {baseImage.Width}x{baseImage.Height}", imageFile.FilePath);
                    LogPerformance(nameof(LoadPreviewWithHistogramAsync), "Load", sw.ElapsedMilliseconds, imageFile.FilePath,
                        $"size={baseImage.Width}x{baseImage.Height}");
                    sw.Restart();

                    _editService.ApplyEdits(baseImage, settings);
                    LogPerformance(nameof(LoadPreviewWithHistogramAsync), "ApplyEdits", sw.ElapsedMilliseconds, imageFile.FilePath);
                    sw.Restart();

                    cancellationToken.ThrowIfCancellationRequested();

                    var bitmap = ConvertToBitmap(baseImage);
                    LogPerformance(nameof(LoadPreviewWithHistogramAsync), "ConvertToBitmap", sw.ElapsedMilliseconds, imageFile.FilePath);
                    sw.Restart();

                    if (!skipHistogram)
                    {
                        _histogramService.CalculateHistogram(baseImage, histogram);
                        LogPerformance(nameof(LoadPreviewWithHistogramAsync), "Histogram", sw.ElapsedMilliseconds, imageFile.FilePath);
                    }

                    LogDebug(nameof(LoadPreviewWithHistogramAsync), "Exit - success", imageFile.FilePath);
                    LogPerformance(nameof(LoadPreviewWithHistogramAsync), "Total", swTotal.ElapsedMilliseconds, imageFile.FilePath);
                    return (bitmap, histogram);
                }
            }
            catch (OperationCanceledException)
            {
                LogDebug(nameof(LoadPreviewWithHistogramAsync), "Cancelled", imageFile.FilePath);
                throw;
            }
            catch (Exception ex)
            {
                LogDebug(nameof(LoadPreviewWithHistogramAsync), $"Failed: {ex.Message}", imageFile.FilePath);
                HandleImageLoadError(ex, imageFile.FilePath);
                return (null, histogram);
            }
        }, cancellationToken);
    }

    public async Task<(Bitmap? preview, HistogramData histogram)> ApplyEditsToPreviewAsync(
        ImageFile imageFile, EditSettings settings, bool skipHistogram = false, CancellationToken cancellationToken = default)
    {
        await imageFile.EnsureCatalogIdAsync(_catalogService);

        return await Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            var swTotal = Stopwatch.StartNew();
            var histogram = new HistogramData();

            LogDebug(nameof(ApplyEditsToPreviewAsync), $"Entry (skipHistogram={skipHistogram})", imageFile.FilePath);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                MagickImage editableImage;

                _previewCacheGate.Wait(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_cachedPreviewImage == null || _cachedPreviewPath != imageFile.FilePath)
                    {
                        _cachedPreviewImage?.Dispose();
                        _cachedPreviewImage = null;
                        _cachedPreviewPath = null;

                        MagickImage? loadedImage = null;
                        try
                        {
                            loadedImage = LoadPreviewImage(imageFile);
                            cancellationToken.ThrowIfCancellationRequested();
                            _cachedPreviewImage = loadedImage;
                            _cachedPreviewPath = imageFile.FilePath;
                            loadedImage = null;
                        }
                        finally
                        {
                            loadedImage?.Dispose();
                        }

                        LogDebug(nameof(ApplyEditsToPreviewAsync),
                            $"Loaded new preview: {_cachedPreviewImage.Width}x{_cachedPreviewImage.Height}",
                            imageFile.FilePath);
                        LogPerformance(nameof(ApplyEditsToPreviewAsync), "LoadPreview", sw.ElapsedMilliseconds, imageFile.FilePath);
                        sw.Restart();
                    }
                    else
                    {
                        LogDebug(nameof(ApplyEditsToPreviewAsync), "Using cached preview", imageFile.FilePath);
                    }

                    // Clone the cached image for editing (original stays pristine)
                    editableImage = new MagickImage(_cachedPreviewImage);
                }
                finally
                {
                    _previewCacheGate.Release();
                }

                using (editableImage)
                {
                    LogPerformance(nameof(ApplyEditsToPreviewAsync), "Clone", sw.ElapsedMilliseconds, imageFile.FilePath);
                    sw.Restart();

                    cancellationToken.ThrowIfCancellationRequested();

                    _editService.ApplyEdits(editableImage, settings);
                    LogPerformance(nameof(ApplyEditsToPreviewAsync), "ApplyEdits", sw.ElapsedMilliseconds, imageFile.FilePath);
                    sw.Restart();

                    cancellationToken.ThrowIfCancellationRequested();

                    if (!skipHistogram)
                    {
                        _histogramService.CalculateHistogram(editableImage, histogram);
                        LogPerformance(nameof(ApplyEditsToPreviewAsync), "Histogram", sw.ElapsedMilliseconds, imageFile.FilePath);
                        sw.Restart();
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    var bitmap = ConvertToBitmap(editableImage);

                    LogDebug(nameof(ApplyEditsToPreviewAsync), "Exit - success", imageFile.FilePath);
                    LogPerformance(nameof(ApplyEditsToPreviewAsync), "ConvertToBitmap", sw.ElapsedMilliseconds, imageFile.FilePath);
                    LogPerformance(nameof(ApplyEditsToPreviewAsync), "Total", swTotal.ElapsedMilliseconds, imageFile.FilePath);
                    return (bitmap, histogram);
                }
            }
            catch (OperationCanceledException)
            {
                LogDebug(nameof(ApplyEditsToPreviewAsync), "Cancelled", imageFile.FilePath);
                throw;
            }
            catch (Exception ex)
            {
                LogDebug(nameof(ApplyEditsToPreviewAsync), $"Failed: {ex.Message}", imageFile.FilePath);
                HandleImageLoadError(ex, imageFile.FilePath);
                return (null, histogram);
            }
        }, cancellationToken);
    }

    public void ClearPreviewCache()
    {
        _previewCacheGate.Wait();
        try
        {
            _cachedPreviewImage?.Dispose();
            _cachedPreviewImage = null;
            _cachedPreviewPath = null;
        }
        finally
        {
            _previewCacheGate.Release();
        }
    }

    private MagickImage LoadPreviewImage(ImageFile imageFile)
    {
        LogDebug(nameof(LoadPreviewImage), $"Entry - IsRaw={imageFile.IsRaw}", imageFile.FilePath);

        if (_previewCache.IsCacheValid(imageFile))
        {
            var cached = _previewCache.LoadFromCache(imageFile);
            if (cached != null)
            {
                LogDebug(nameof(LoadPreviewImage), $"Preview cache hit: {cached.Width}x{cached.Height}", imageFile.FilePath);
                return cached;
            }
        }

        MagickImage? result = null;

        if (imageFile.IsRaw && _rawService.IsAvailable)
        {
            LogDebug(nameof(LoadPreviewImage), "Using LibRaw half-size decode for RAW", imageFile.FilePath);
            result = _rawService.DecodeHalfSize(imageFile.FilePath);

            if (result != null)
            {
                LogDebug(nameof(LoadPreviewImage), $"LibRaw decoded: {result.Width}x{result.Height}", imageFile.FilePath);

                var orientation = GetExifOrientation(imageFile.FilePath);
                bool orientationSwapsDimensions = orientation >= 5 && orientation <= 8;

                bool alreadyOriented = false;
                if (orientationSwapsDimensions)
                {
                    alreadyOriented = result.Width < result.Height;
                    LogDebug(nameof(LoadPreviewImage), $"EXIF orientation {orientation} swaps dimensions, decoded is {(result.Width < result.Height ? "portrait" : "landscape")}, alreadyOriented={alreadyOriented}", imageFile.FilePath);
                }

                if (orientation != 1 && !alreadyOriented)
                {
                    LogDebug(nameof(LoadPreviewImage), $"Applying EXIF orientation: {orientation}", imageFile.FilePath);
                    ApplyExifOrientation(result, orientation);
                }
                else if (alreadyOriented)
                {
                    LogDebug(nameof(LoadPreviewImage), $"Skipping EXIF orientation (already applied by LibRaw)", imageFile.FilePath);
                }

                if (result.Width > PreviewMaxDimension || result.Height > PreviewMaxDimension)
                {
                    ResizeToMaxDimension(result, PreviewMaxDimension);
                    LogDebug(nameof(LoadPreviewImage), $"After resize: {result.Width}x{result.Height}", imageFile.FilePath);
                }
            }
            else
            {
                LogDebug(nameof(LoadPreviewImage), "LibRaw decode failed", imageFile.FilePath);
            }
        }

        if (result == null)
        {
            LogDebug(nameof(LoadPreviewImage), "Loading file with MagickImage", imageFile.FilePath);
            var settings = new MagickReadSettings
            {
                Width = PreviewMaxDimension,
                Height = PreviewMaxDimension
            };

            var ext = imageFile.Extension.ToUpperInvariant();
            if (ext == ".JPG" || ext == ".JPEG")
            {
                ApplyJpegSizeHint(settings, PreviewMaxDimension);
            }

            result = new MagickImage(imageFile.FilePath, settings);
            LogDebug(nameof(LoadPreviewImage), $"Loaded: {result.Width}x{result.Height}", imageFile.FilePath);

            if (result.Width > PreviewMaxDimension || result.Height > PreviewMaxDimension)
            {
                ResizeToMaxDimension(result, PreviewMaxDimension);
            }

            result.AutoOrient();
        }

        LogDebug(nameof(LoadPreviewImage), "Saving to preview cache", imageFile.FilePath);
        _previewCache.QueueSaveToCache(imageFile, result);

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        await _previewCacheGate.WaitAsync();
        try
        {
            _cachedPreviewImage?.Dispose();
            _cachedPreviewImage = null;
            _cachedPreviewPath = null;
        }
        finally
        {
            _previewCacheGate.Release();
        }
        await _previewCache.DisposeAsync();
    }
}

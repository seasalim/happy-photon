using System.Diagnostics;
using Avalonia.Media.Imaging;
using ImageMagick;
using HappyPhoton.Models;
using static HappyPhoton.Services.ImageServiceHelpers;
using static HappyPhoton.Services.BitmapConversionService;

namespace HappyPhoton.Services;

/// <summary>
/// Service for loading, generating, and caching image thumbnails.
/// </summary>
public class ThumbnailService : IAsyncDisposable
{
    private const int ThumbnailSize = 150;

    private readonly ICatalogService _catalogService;
    private readonly ThumbnailCacheService _thumbnailCache;
    private readonly IRawProcessingService _rawService;
    private readonly EditApplicationService _editService;

    public ThumbnailService(
        ICatalogService catalogService,
        IRawProcessingService rawService,
        EditApplicationService editService)
    {
        _catalogService = catalogService;
        _thumbnailCache = new ThumbnailCacheService(catalogService);
        _rawService = rawService;
        _editService = editService;
    }

    public async Task<Bitmap?> LoadThumbnailAsync(ImageFile imageFile, CancellationToken cancellationToken = default)
    {
        var bitmap = await LoadUneditedThumbnailAsync(imageFile, cancellationToken);
        if (bitmap == null || !imageFile.EditSettings.HasEdits)
        {
            return bitmap;
        }

        return await Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogDebug(nameof(LoadThumbnailAsync), "Applying edits to thumbnail", imageFile.FilePath);
                var edited = ApplyEditsToThumbnail(bitmap, imageFile.EditSettings);
                if (edited == null)
                {
                    return bitmap;
                }
                bitmap.Dispose();
                return edited;
            }
            catch (OperationCanceledException)
            {
                bitmap.Dispose();
                return null;
            }
            catch (Exception ex)
            {
                LogDebug(nameof(LoadThumbnailAsync), $"Failed to apply edits: {ex.Message}", imageFile.FilePath);
                return bitmap;
            }
        });
    }

    public async Task<Bitmap?> LoadUneditedThumbnailAsync(
        ImageFile imageFile,
        CancellationToken cancellationToken = default)
    {
        await imageFile.EnsureCatalogIdAsync(_catalogService);

        return await Task.Run(() =>
        {
            var swTotal = Stopwatch.StartNew();
            string? source = null;
            LogDebug(nameof(LoadThumbnailAsync), "Entry", imageFile.FilePath);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                Bitmap? bitmap = null;

                if (_thumbnailCache.IsCacheValid(imageFile))
                {
                    var swCache = Stopwatch.StartNew();
                    var cached = _thumbnailCache.LoadFromCache(imageFile);
                    if (cached != null)
                    {
                        LogDebug(nameof(LoadThumbnailAsync), "Cache hit - returning cached thumbnail", imageFile.FilePath);
                        LogPerformance(nameof(LoadThumbnailAsync), "CacheHit", swCache.ElapsedMilliseconds, imageFile.FilePath);
                        bitmap = cached;
                        source = "Cache";
                    }
                    else
                    {
                        LogDebug(nameof(LoadThumbnailAsync), "Cache miss - cache file invalid or corrupt", imageFile.FilePath);
                        LogPerformance(nameof(LoadThumbnailAsync), "CacheMiss", swCache.ElapsedMilliseconds, imageFile.FilePath);
                    }
                }
                else
                {
                    LogDebug(nameof(LoadThumbnailAsync), "Cache miss - no valid cache", imageFile.FilePath);
                }

                if (bitmap == null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // For RAW files, try embedded preview first
                    if (imageFile.IsRaw)
                    {
                        LogDebug(nameof(LoadThumbnailAsync), "IsRaw=true - trying embedded preview extraction", imageFile.FilePath);
                        var swEmbedded = Stopwatch.StartNew();
                        bitmap = TryExtractEmbeddedThumbnail(imageFile.FilePath, cancellationToken);
                        LogPerformance(nameof(LoadThumbnailAsync), "EmbeddedPreview", swEmbedded.ElapsedMilliseconds, imageFile.FilePath,
                            $"result={(bitmap != null ? "hit" : "miss")}");
                        source = "EmbeddedPreview";
                    }

                    // Fall back to full decode if no bitmap yet
                    if (bitmap == null)
                    {
                        LogDebug(nameof(LoadThumbnailAsync), imageFile.IsRaw 
                            ? "Embedded extraction failed - falling back to full decode" 
                            : "IsRaw=false - using full decode", imageFile.FilePath);
                        cancellationToken.ThrowIfCancellationRequested();
                        var swFull = Stopwatch.StartNew();
                        bitmap = GenerateThumbnailFromFullImage(imageFile.FilePath, cancellationToken);
                        LogPerformance(nameof(LoadThumbnailAsync), "FullDecode", swFull.ElapsedMilliseconds, imageFile.FilePath,
                            $"result={(bitmap != null ? "ok" : "null")}");
                        source = "FullDecode";
                    }

                    if (bitmap != null && !cancellationToken.IsCancellationRequested)
                    {
                        var swSave = Stopwatch.StartNew();
                        _thumbnailCache.QueueSaveToCache(imageFile, bitmap);
                        LogPerformance(nameof(LoadThumbnailAsync), "CacheQueue", swSave.ElapsedMilliseconds, imageFile.FilePath);
                    }
                }

                LogDebug(nameof(LoadThumbnailAsync), $"Exit - source={source ?? "None"}", imageFile.FilePath);
                LogPerformance(nameof(LoadThumbnailAsync), "Total", swTotal.ElapsedMilliseconds, imageFile.FilePath,
                    $"source={source ?? "None"}");
                return bitmap;
            }
            catch (OperationCanceledException)
            {
                LogDebug(nameof(LoadThumbnailAsync), "Cancelled", imageFile.FilePath);
                return null;
            }
            catch (Exception ex)
            {
                LogDebug(nameof(LoadThumbnailAsync), $"Failed: {ex.Message}", imageFile.FilePath);
                return null;
            }
        }, cancellationToken);
    }

    private Bitmap? TryExtractEmbeddedThumbnail(string filePath, CancellationToken cancellationToken)
    {
        if (_rawService.IsAvailable)
        {
            LogDebug(nameof(TryExtractEmbeddedThumbnail), "Step 0: Trying LibRaw thumbnail extraction", filePath);
            var libRawResult = TryExtractLibRawThumbnail(filePath, cancellationToken);
            if (libRawResult != null)
            {
                LogDebug(nameof(TryExtractEmbeddedThumbnail), "LibRaw thumbnail succeeded", filePath);
                return libRawResult;
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        LogDebug(nameof(TryExtractEmbeddedThumbnail), "Step 1: Trying EXIF thumbnail", filePath);

        var exifResult = TryExtractExifThumbnail(filePath, cancellationToken);
        if (exifResult != null)
        {
            LogDebug(nameof(TryExtractEmbeddedThumbnail), "EXIF thumbnail succeeded", filePath);
            return exifResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        LogDebug(nameof(TryExtractEmbeddedThumbnail), "Step 2: Trying embedded JPEG", filePath);

        exifResult = TryExtractEmbeddedJpegThumbnail(filePath, cancellationToken);
        if (exifResult != null)
        {
            LogDebug(nameof(TryExtractEmbeddedThumbnail), "Embedded JPEG succeeded", filePath);
            return exifResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        LogDebug(nameof(TryExtractEmbeddedThumbnail), "Step 3: Trying preview frame", filePath);

        exifResult = TryExtractPreviewFrame(filePath, cancellationToken);
        if (exifResult != null)
        {
            LogDebug(nameof(TryExtractEmbeddedThumbnail), "Preview frame succeeded", filePath);
        }
        else
        {
            LogDebug(nameof(TryExtractEmbeddedThumbnail), "All extraction methods failed", filePath);
        }
        return exifResult;
    }

    private Bitmap? TryExtractLibRawThumbnail(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var jpegData = _rawService.ExtractThumbnail(filePath);
            if (jpegData == null || jpegData.Length == 0)
            {
                LogDebug(nameof(TryExtractLibRawThumbnail), "LibRaw returned no thumbnail", filePath);
                return null;
            }

            LogDebug(nameof(TryExtractLibRawThumbnail), $"LibRaw thumbnail: {jpegData.Length / 1024}KB", filePath);

            cancellationToken.ThrowIfCancellationRequested();

            using var image = new MagickImage(jpegData);
            LogDebug(nameof(TryExtractLibRawThumbnail), $"Decoded: {image.Width}x{image.Height}", filePath);

            if (image.Orientation == OrientationType.Undefined)
            {
                var orientation = GetExifOrientation(filePath);
                LogDebug(nameof(TryExtractLibRawThumbnail), $"Applying EXIF orientation from RAW: {orientation}", filePath);
                ApplyExifOrientation(image, orientation);
            }
            else
            {
                image.AutoOrient();
            }

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
            LogDebug(nameof(TryExtractLibRawThumbnail), $"Failed: {ex.Message}", filePath);
            return null;
        }
    }

    private Bitmap? TryExtractEmbeddedJpegThumbnail(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            LogDebug(nameof(TryExtractEmbeddedJpegThumbnail), "Extracting embedded JPEG", filePath);

            var jpegData = EmbeddedJpegExtractor.ExtractEmbeddedJpeg(filePath);
            if (jpegData == null)
            {
                LogDebug(nameof(TryExtractEmbeddedJpegThumbnail), "No embedded JPEG found", filePath);
                return null;
            }

            LogDebug(nameof(TryExtractEmbeddedJpegThumbnail), $"Embedded JPEG: {jpegData.Length / 1024}KB", filePath);

            cancellationToken.ThrowIfCancellationRequested();

            using var image = new MagickImage(jpegData);
            LogDebug(nameof(TryExtractEmbeddedJpegThumbnail), $"Decoded: {image.Width}x{image.Height}", filePath);
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
            LogDebug(nameof(TryExtractEmbeddedJpegThumbnail), $"Failed: {ex.Message}", filePath);
            return null;
        }
    }

    private Bitmap? TryExtractExifThumbnail(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            LogDebug(nameof(TryExtractExifThumbnail), "Pinging file for EXIF profile", filePath);

            using var image = new MagickImage();
            image.Ping(filePath);

            cancellationToken.ThrowIfCancellationRequested();

            var thumbnail = ExifThumbnailDecoder.TryDecode(
                image, ThumbnailSize, cancellationToken);
            if (thumbnail != null)
            {
                LogDebug(nameof(TryExtractExifThumbnail), "EXIF thumbnail found", filePath);
            }
            return thumbnail;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDebug(nameof(TryExtractExifThumbnail), $"Failed: {ex.Message}", filePath);
            return null;
        }
    }

    private Bitmap? TryExtractPreviewFrame(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            LogDebug(nameof(TryExtractPreviewFrame), "Trying MagickReadSettings with size hints (1024x1024)", filePath);

            var settings = new MagickReadSettings
            {
                Width = 1024,
                Height = 1024
            };

            cancellationToken.ThrowIfCancellationRequested();

            using var image = new MagickImage(filePath, settings);
            LogDebug(nameof(TryExtractPreviewFrame), $"Loaded: {image.Width}x{image.Height}", filePath);
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
            LogDebug(nameof(TryExtractPreviewFrame), $"Failed: {ex.Message}", filePath);
            HandleImageLoadError(ex, filePath);
            return null;
        }
    }

    private Bitmap? GenerateThumbnailFromFullImage(string filePath, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var ext = Path.GetExtension(filePath).ToUpperInvariant();
            if (ext == ".JPG" || ext == ".JPEG")
            {
                try
                {
                    var jpegBitmap = JpegThumbnailDecoder.Decode(
                        filePath, ThumbnailSize, cancellationToken);
                    LogPerformance(nameof(GenerateThumbnailFromFullImage),
                        "JpegDecode", sw.ElapsedMilliseconds, filePath,
                        $"thumb={jpegBitmap.PixelSize.Width}x{jpegBitmap.PixelSize.Height}");
                    return jpegBitmap;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogDebug(nameof(GenerateThumbnailFromFullImage),
                        $"Platform JPEG decode failed, using Magick.NET: {ex.Message}", filePath);
                    sw.Restart();
                }
            }

            var settings = new MagickReadSettings();
            if (ext == ".JPG" || ext == ".JPEG")
            {
                ApplyJpegSizeHint(settings, ThumbnailSize * 3);
            }
            using var image = new MagickImage(filePath, settings);
            LogPerformance(nameof(GenerateThumbnailFromFullImage), "Load", sw.ElapsedMilliseconds, filePath,
                $"size={image.Width}x{image.Height}");
            sw.Restart();

            cancellationToken.ThrowIfCancellationRequested();

            image.AutoOrient();
            ApplyThumbnailSize(image, ThumbnailSize);
            LogPerformance(nameof(GenerateThumbnailFromFullImage), "Resize", sw.ElapsedMilliseconds, filePath,
                $"thumb={image.Width}x{image.Height}");
            sw.Restart();

            cancellationToken.ThrowIfCancellationRequested();

            var bitmap = ConvertToBitmap(image);
            LogPerformance(nameof(GenerateThumbnailFromFullImage), "ConvertToBitmap", sw.ElapsedMilliseconds, filePath);
            return bitmap;
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

    public ValueTask DisposeAsync() => _thumbnailCache.DisposeAsync();

    private Bitmap? ApplyEditsToThumbnail(Bitmap? bitmap, EditSettings settings)
    {
        if (bitmap == null)
            return null;

        using var image = ConvertToMagickImage(bitmap);
        _editService.ApplyEdits(image, settings);

        return ConvertToBitmap(image);
    }
}

using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.BitmapConversionService;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

internal sealed class EmbeddedPreviewExtractor
{
    private readonly IRawProcessingService _rawService;
    private readonly int _thumbnailSize;

    public EmbeddedPreviewExtractor(
        IRawProcessingService rawService,
        int thumbnailSize)
    {
        _rawService = rawService;
        _thumbnailSize = thumbnailSize;
    }

    public Bitmap? TryExtract(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (_rawService.IsAvailable)
        {
            var thumbnail = TryExtractLibRaw(filePath, cancellationToken);
            if (thumbnail != null) return thumbnail;
            cancellationToken.ThrowIfCancellationRequested();
        }

        return TryExtractExif(filePath, cancellationToken) ??
            TryExtractEmbeddedJpeg(filePath, cancellationToken) ??
            TryExtractPreviewFrame(filePath, cancellationToken);
    }

    private Bitmap? TryExtractLibRaw(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var thumbnail = _rawService.ExtractThumbnail(filePath);
            if (thumbnail?.EncodedBytes is not { Length: > 0 } encodedBytes)
            {
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();

            using var image = new MagickImage(encodedBytes);
            if (image.Orientation == OrientationType.Undefined)
            {
                ApplyExifOrientation(image, GetExifOrientation(filePath));
            }
            else
            {
                image.AutoOrient();
            }
            NormalizeLibRawPreview(image, thumbnail);
            ApplyThumbnailSize(image, _thumbnailSize);
            cancellationToken.ThrowIfCancellationRequested();
            return ConvertToBitmap(image);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static void NormalizeLibRawPreview(
        MagickImage image,
        RawThumbnailData thumbnail)
    {
        var difference = CropGeometry.RelativeAspectRatioDifference(
            thumbnail.VisibleSourceWidth ?? 0,
            thumbnail.VisibleSourceHeight ?? 0,
            image.Width,
            image.Height);
        if (difference is null or <= 0.03)
        {
            return;
        }

        var crop = CropGeometry.CenterCropToAspect(
            image.Width,
            image.Height,
            thumbnail.VisibleSourceWidth!.Value,
            thumbnail.VisibleSourceHeight!.Value);
        if (crop == null)
        {
            return;
        }

        image.Crop(new MagickGeometry(
            crop.Value.X,
            crop.Value.Y,
            crop.Value.Width,
            crop.Value.Height));
        image.ResetPage();
    }

    private Bitmap? TryExtractExif(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var image = new MagickImage();
            image.Ping(filePath);
            cancellationToken.ThrowIfCancellationRequested();
            return ExifThumbnailDecoder.TryDecode(
                image,
                _thumbnailSize,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private Bitmap? TryExtractEmbeddedJpeg(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var jpegData = EmbeddedJpegExtractor.ExtractEmbeddedJpeg(filePath);
            if (jpegData == null) return null;
            cancellationToken.ThrowIfCancellationRequested();

            using var image = new MagickImage(jpegData);
            image.AutoOrient();
            ApplyThumbnailSize(image, _thumbnailSize);
            cancellationToken.ThrowIfCancellationRequested();
            return ConvertToBitmap(image);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private Bitmap? TryExtractPreviewFrame(
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = new MagickReadSettings
            {
                Width = 1024,
                Height = 1024
            };
            cancellationToken.ThrowIfCancellationRequested();

            using var image = new MagickImage(filePath, settings);
            image.AutoOrient();
            ApplyThumbnailSize(image, _thumbnailSize);
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
}

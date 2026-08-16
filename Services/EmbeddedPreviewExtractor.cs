using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.BitmapConversionService;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

internal sealed class EmbeddedPreviewExtractor
{
    private readonly IRawProcessingService _rawService;

    public EmbeddedPreviewExtractor(IRawProcessingService rawService) =>
        _rawService = rawService;

    public Bitmap? TryExtract(
        string filePath,
        int generationDimension,
        CancellationToken cancellationToken)
    {
        Bitmap? best = null;
        if (_rawService.IsAvailable)
        {
            best = ChooseBest(
                best,
                TryExtractLibRaw(
                    filePath,
                    generationDimension,
                    cancellationToken));
            if (Meets(best, generationDimension)) return best;
            cancellationToken.ThrowIfCancellationRequested();
        }

        best = ChooseBest(
            best,
            TryExtractExif(filePath, generationDimension, cancellationToken));
        if (Meets(best, generationDimension)) return best;
        best = ChooseBest(
            best,
            TryExtractEmbeddedJpeg(
                filePath,
                generationDimension,
                cancellationToken));
        return best;
    }

    private Bitmap? TryExtractLibRaw(
        string filePath,
        int generationDimension,
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
            ApplyThumbnailSize(image, generationDimension);
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
        int generationDimension,
        CancellationToken cancellationToken)
    {
        try
        {
            using var image = new MagickImage();
            image.Ping(filePath);
            cancellationToken.ThrowIfCancellationRequested();
            return ExifThumbnailDecoder.TryDecode(
                image,
                generationDimension,
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
        int generationDimension,
        CancellationToken cancellationToken)
    {
        try
        {
            var jpegData = EmbeddedJpegExtractor.ExtractEmbeddedJpeg(filePath);
            if (jpegData == null) return null;
            cancellationToken.ThrowIfCancellationRequested();

            using var image = new MagickImage(jpegData);
            image.AutoOrient();
            ApplyThumbnailSize(image, generationDimension);
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

    private static Bitmap? ChooseBest(Bitmap? current, Bitmap? candidate)
    {
        if (candidate == null) return current;
        if (current == null) return candidate;
        if (LongEdge(candidate) > LongEdge(current))
        {
            current.Dispose();
            return candidate;
        }

        candidate.Dispose();
        return current;
    }

    private static bool Meets(Bitmap? bitmap, int dimension) =>
        bitmap != null && LongEdge(bitmap) >= dimension;

    private static int LongEdge(Bitmap bitmap) =>
        Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
}

using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class ExifThumbnailDecoder
{
    private const double MaximumAspectRatioDifference = 0.03;

    public static Bitmap? TryDecode(
        MagickImage imageInfo,
        int maxDimension,
        CancellationToken cancellationToken)
    {
        try
        {
            using var thumbnailData = imageInfo.GetExifProfile()?.CreateThumbnail();
            if (thumbnailData == null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var thumbnail = new MagickImage(thumbnailData.ToByteArray());
            if (!HasCompatibleAspectRatio(imageInfo, thumbnail))
            {
                return null;
            }

            if (thumbnail.Orientation == OrientationType.Undefined)
            {
                ImageServiceHelpers.ApplyExifOrientation(
                    thumbnail, NormalizeOrientation((int)imageInfo.Orientation));
            }
            else
            {
                thumbnail.AutoOrient();
            }

            if (thumbnail.Width > maxDimension || thumbnail.Height > maxDimension)
            {
                BitmapConversionService.ApplyThumbnailSize(thumbnail, maxDimension);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return BitmapConversionService.ConvertToBitmap(thumbnail);
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

    private static int NormalizeOrientation(int orientation) =>
        orientation is >= 1 and <= 8 ? orientation : 1;

    private static bool HasCompatibleAspectRatio(MagickImage source, MagickImage thumbnail) =>
        HasCompatibleAspectRatio(
            source.Width,
            source.Height,
            thumbnail.Width,
            thumbnail.Height);

    internal static bool HasCompatibleAspectRatio(
        long sourceWidth,
        long sourceHeight,
        long thumbnailWidth,
        long thumbnailHeight)
    {
        var difference = CropGeometry.RelativeAspectRatioDifference(
            sourceWidth,
            sourceHeight,
            thumbnailWidth,
            thumbnailHeight);
        return difference is <= MaximumAspectRatioDifference;
    }
}

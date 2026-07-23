using Avalonia.Media.Imaging;
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

    private static bool HasCompatibleAspectRatio(MagickImage source, MagickImage thumbnail)
    {
        if (source.Width == 0 || source.Height == 0 || thumbnail.Width == 0 || thumbnail.Height == 0)
        {
            return false;
        }

        var sourceRatio = Math.Max(source.Width, source.Height) /
            (double)Math.Min(source.Width, source.Height);
        var thumbnailRatio = Math.Max(thumbnail.Width, thumbnail.Height) /
            (double)Math.Min(thumbnail.Width, thumbnail.Height);
        return Math.Abs(sourceRatio - thumbnailRatio) / sourceRatio <=
            MaximumAspectRatioDifference;
    }
}

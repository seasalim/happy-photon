using Avalonia.Media.Imaging;
using ImageMagick;

namespace HappyPhoton.Services;

internal static class JpegThumbnailDecoder
{
    public static Bitmap Decode(string filePath, int maxDimension, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var imageInfo = new MagickImage();
        imageInfo.Ping(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        var embeddedThumbnail = ExifThumbnailDecoder.TryDecode(
            imageInfo, maxDimension, cancellationToken);
        if (embeddedThumbnail != null)
        {
            return embeddedThumbnail;
        }

        using var stream = File.OpenRead(filePath);
        var bitmap = DecodeAtSize(
            stream,
            (int)imageInfo.Width,
            (int)imageInfo.Height,
            maxDimension);

        var orientation = NormalizeOrientation((int)imageInfo.Orientation);
        if (orientation == 1)
        {
            return bitmap;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BgraBitmapOrientation.ApplyExifOrientation(bitmap, orientation);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    private static Bitmap DecodeAtSize(Stream stream, int width, int height, int maxDimension)
    {
        if (width <= maxDimension && height <= maxDimension)
        {
            return new Bitmap(stream);
        }

        return width >= height
            ? Bitmap.DecodeToWidth(stream, maxDimension, BitmapInterpolationMode.MediumQuality)
            : Bitmap.DecodeToHeight(stream, maxDimension, BitmapInterpolationMode.MediumQuality);
    }

    private static int NormalizeOrientation(int orientation) =>
        orientation is >= 1 and <= 8 ? orientation : 1;

    internal static (int X, int Y) MapPixel(int x, int y, int width, int height, int orientation) =>
        BgraBitmapOrientation.MapPixel(x, y, width, height, orientation);
}

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
            return ApplyOrientation(bitmap, orientation);
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

    private static unsafe Bitmap ApplyOrientation(Bitmap source, int orientation)
    {
        var sourceSize = source.PixelSize;
        var swapsDimensions = orientation is >= 5 and <= 8;
        var destinationSize = swapsDimensions
            ? new PixelSize(sourceSize.Height, sourceSize.Width)
            : sourceSize;

        using var readableSource = new WriteableBitmap(
            sourceSize,
            source.Dpi,
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        var destination = new WriteableBitmap(
            destinationSize,
            source.Dpi,
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        try
        {
            using var sourceBuffer = readableSource.Lock();
            source.CopyPixels(sourceBuffer);
            using var destinationBuffer = destination.Lock();

            for (var sourceY = 0; sourceY < sourceSize.Height; sourceY++)
            {
                for (var sourceX = 0; sourceX < sourceSize.Width; sourceX++)
                {
                    var (destinationX, destinationY) = MapPixel(
                        sourceX,
                        sourceY,
                        sourceSize.Width,
                        sourceSize.Height,
                        orientation);
                    var sourcePixel = (uint*)((byte*)sourceBuffer.Address +
                        sourceY * sourceBuffer.RowBytes + sourceX * 4);
                    var destinationPixel = (uint*)((byte*)destinationBuffer.Address +
                        destinationY * destinationBuffer.RowBytes + destinationX * 4);
                    *destinationPixel = *sourcePixel;
                }
            }

            return destination;
        }
        catch
        {
            destination.Dispose();
            throw;
        }
    }

    internal static (int X, int Y) MapPixel(
        int x,
        int y,
        int width,
        int height,
        int orientation) => orientation switch
    {
        2 => (width - 1 - x, y),
        3 => (width - 1 - x, height - 1 - y),
        4 => (x, height - 1 - y),
        5 => (y, x),
        6 => (height - 1 - y, x),
        7 => (height - 1 - y, width - 1 - x),
        8 => (y, width - 1 - x),
        _ => (x, y)
    };
}

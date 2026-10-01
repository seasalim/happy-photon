using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HappyPhoton.Services;

internal static class BgraBitmapOrientation
{
    public static unsafe Bitmap ApplyExifOrientation(Bitmap source, int orientation)
    {
        var sourceSize = source.PixelSize;
        var swapsDimensions = orientation is >= 5 and <= 8;
        var destinationSize = swapsDimensions
            ? new PixelSize(sourceSize.Height, sourceSize.Width)
            : sourceSize;

        var writableSource = source.Format == PixelFormat.Bgra8888 ? source as WriteableBitmap : null;
        var alpha = source.AlphaFormat ?? AlphaFormat.Premul;
        using var readableSource = writableSource == null ? new WriteableBitmap(
            sourceSize,
            source.Dpi,
            PixelFormat.Bgra8888,
            alpha) : null;
        var destination = new WriteableBitmap(
            destinationSize,
            source.Dpi,
            PixelFormat.Bgra8888,
            alpha);

        try
        {
            using var sourceBuffer = (writableSource ?? readableSource!).Lock();
            if (writableSource == null) source.CopyPixels(sourceBuffer);
            using var destinationBuffer = destination.Lock();

            var sourceAddress = (byte*)sourceBuffer.Address;
            var destinationAddress = (byte*)destinationBuffer.Address;
            var sourceStride = sourceBuffer.RowBytes;
            var destinationStride = destinationBuffer.RowBytes;

            var (originX, originY) = MapPixel(0, 0, sourceSize.Width, sourceSize.Height, orientation);
            var (nextX, nextY) = MapPixel(1, 0, sourceSize.Width, sourceSize.Height, orientation);
            var destinationStep = (nextY - originY) * destinationStride + (nextX - originX) * 4;

            for (var sourceY = 0; sourceY < sourceSize.Height; sourceY++)
            {
                var (destinationX, destinationY) = MapPixel(
                    0, sourceY, sourceSize.Width, sourceSize.Height, orientation);
                var sourcePixel = (uint*)(sourceAddress + sourceY * sourceStride);
                var destinationPixel = destinationAddress + destinationY * destinationStride + destinationX * 4;

                for (var sourceX = 0; sourceX < sourceSize.Width; sourceX++)
                {
                    *(uint*)destinationPixel = sourcePixel[sourceX];
                    destinationPixel += destinationStep;
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

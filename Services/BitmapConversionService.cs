using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ImageMagick;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Static utilities for bitmap conversion and manipulation.
/// </summary>
public static class BitmapConversionService
{
    public static byte[] CopyBgraPixels(Bitmap bitmap)
    {
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        using var copy = new WriteableBitmap(
            bitmap.PixelSize,
            bitmap.Dpi,
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var framebuffer = copy.Lock();
        bitmap.CopyPixels(framebuffer);

        var pixels = new byte[width * height * 4];
        unsafe
        {
            fixed (byte* destination = pixels)
            {
                var source = (byte*)framebuffer.Address;
                var rowBytes = width * 4;
                for (var y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(
                        source + y * framebuffer.RowBytes,
                        destination + y * rowBytes,
                        rowBytes,
                        rowBytes);
                }
            }
        }
        return pixels;
    }

    public static MagickImage ConvertToMagickImage(Bitmap bitmap)
    {
        var pixels = CopyBgraPixels(bitmap);
        var settings = new PixelReadSettings(
            (uint)bitmap.PixelSize.Width,
            (uint)bitmap.PixelSize.Height,
            StorageType.Char,
            PixelMapping.BGRA);
        return new MagickImage(pixels, settings);
    }

    /// <summary>
    /// Encodes an Avalonia bitmap for non-UI image processing.
    /// </summary>
    public static byte[] CreateEncodedSnapshot(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Converts a MagickImage to an Avalonia Bitmap using direct pixel copy.
    /// </summary>
    public static Bitmap? ConvertToBitmap(MagickImage image)
    {
        var width = (int)image.Width;
        var height = (int)image.Height;

        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            Avalonia.Platform.PixelFormat.Bgra8888,
            Avalonia.Platform.AlphaFormat.Unpremul);

        using (var framebuffer = bitmap.Lock())
        {
            var pixels = image.GetPixelsUnsafe();
            var bytes = pixels.ToByteArray(PixelMapping.BGRA);

            if (bytes != null)
            {
                unsafe
                {
                    fixed (byte* src = bytes)
                    {
                        var srcRowBytes = width * 4;
                        var dstPtr = (byte*)framebuffer.Address;

                        for (int y = 0; y < height; y++)
                        {
                            Buffer.MemoryCopy(
                                src + y * srcRowBytes,
                                dstPtr + y * framebuffer.RowBytes,
                                framebuffer.RowBytes,
                                srcRowBytes);
                        }
                    }
                }
            }
        }

        return bitmap;
    }

    /// <summary>
    /// Resizes a MagickImage to fit within the specified maximum dimension while preserving aspect ratio.
    /// </summary>
    public static void ResizeToMaxDimension(MagickImage image, int maxDimension)
    {
        if (image.Width <= (uint)maxDimension && image.Height <= (uint)maxDimension)
            return;

        var size = new MagickGeometry((uint)maxDimension, (uint)maxDimension)
        {
            IgnoreAspectRatio = false
        };
        image.Resize(size);
    }

    /// <summary>
    /// Applies thumbnail sizing to a MagickImage using optimized thumbnail algorithm.
    /// </summary>
    public static void ApplyThumbnailSize(MagickImage image, int size)
    {
        var geometry = new MagickGeometry((uint)size, (uint)size) { IgnoreAspectRatio = false };
        image.Thumbnail(geometry);
    }

    /// <summary>
    /// Applies JPEG size hint for faster loading of large JPEGs at reduced resolution.
    /// </summary>
    public static void ApplyJpegSizeHint(MagickReadSettings settings, int maxDimension)
    {
        settings.SetDefine(MagickFormat.Jpeg, "size", $"{maxDimension}x{maxDimension}");
    }
}

using ImageMagick;

namespace HappyPhoton.MlSpike;

public static class MaskFiles
{
    public static void Write(string path, byte[] pixels, int width, int height)
    {
        // Magick.NET has no single-channel PixelMapping; expand to RGB, store as 8-bit grayscale.
        var rgb = new byte[checked(pixels.Length * 3)];
        for (var i = 0; i < pixels.Length; i++)
            rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = pixels[i];
        using var image = new MagickImage(rgb,
            new PixelReadSettings((uint)width, (uint)height, StorageType.Char, PixelMapping.RGB));
        image.ColorType = ColorType.Grayscale;
        image.Depth = 8;
        using var stream = LocalFiles.Create(path);
        image.Write(stream, MagickFormat.Png);
    }

    public static (byte[] Pixels, int Width, int Height) Read(string path)
    {
        LocalFiles.Check(path);
        using var image = new MagickImage(path);
        using var pixels = image.GetPixels();
        var rgb = pixels.ToByteArray(PixelMapping.RGB) ?? throw new InvalidDataException("Unreadable mask.");
        var result = new byte[checked((int)(image.Width * image.Height))];
        for (var i = 0; i < result.Length; i++)
        {
            if (rgb[i * 3] is not (0 or 255) || rgb[i * 3] != rgb[i * 3 + 1] || rgb[i * 3] != rgb[i * 3 + 2])
                throw new InvalidDataException("Mask must be binary grayscale.");
            result[i] = rgb[i * 3];
        }
        return (result, (int)image.Width, (int)image.Height);
    }
}

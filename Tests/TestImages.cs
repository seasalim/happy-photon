using ImageMagick;

namespace HappyPhoton.Tests;

internal static class TestImages
{
    public static void WriteJpeg(
        string path,
        MagickColor? color = null,
        uint width = 16,
        uint height = 16)
    {
        using var image = new MagickImage(color ?? MagickColors.Gray, width, height);
        image.Write(path, MagickFormat.Jpeg);
    }
}

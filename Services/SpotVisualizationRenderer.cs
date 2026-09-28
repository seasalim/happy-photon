using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HappyPhoton.Services;

/// <summary>Display-only high pass over an owned BGRA viewport, never a render input.</summary>
public static class SpotVisualizationRenderer
{
    public static unsafe Bitmap Render(byte[] pixels, int width, int height, double threshold)
    {
        var luma = new float[width * height];
        var horizontal = new float[luma.Length];
        var gain = (float)Math.Pow(2, Math.Clamp(threshold, 0, 100) / 10);

        for (var i = 0; i < luma.Length; i++)
            luma[i] = (pixels[i * 4] + 2 * pixels[i * 4 + 1] + pixels[i * 4 + 2]) * .25f;

        for (var y = 0; y < height; y++)
        {
            var row = y * width;

            for (var x = 0; x < width; x++)
                horizontal[row + x] = (luma[row + Math.Max(0, x - 1)] + 2 * luma[row + x] +
                    luma[row + Math.Min(width - 1, x + 1)]) * .25f;
        }

        for (var y = 0; y < height; y++)
        {
            var above = Math.Max(0, y - 1) * width;
            var below = Math.Min(height - 1, y + 1) * width;

            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var blur = (horizontal[above + x] + 2 * horizontal[i] + horizontal[below + x]) * .25f;
                var value = (byte)Math.Min(255, Math.Abs(luma[i] - blur) * gain);
                pixels[i * 4] = value;
                pixels[i * 4 + 1] = value;
                pixels[i * 4 + 2] = value;
                pixels[i * 4 + 3] = 255;
            }
        }

        fixed (byte* address = pixels)
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, (IntPtr)address,
                new PixelSize(width, height), new Vector(96, 96), width * 4);
        }
    }
}

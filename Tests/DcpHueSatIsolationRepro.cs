using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DcpHueSatIsolationRepro
{
    [Fact]
    public void Apply_KeepsEveryBandLit()
    {
        const int Width = 1600;
        const int Height = 1200;
        using var image = new MagickImage(
            MagickColors.Black,
            Width,
            Height);
        image.ColorSpace = ColorSpace.RGB;
        using (var pixels = image.GetPixelsUnsafe())
        {
            var layout = RenderKernelSupport.GetLayout(pixels);
            var values = new ushort[Width * Height * layout.Channels];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = 20000;
            }
            pixels.SetPixels(values);
        }

        var table = new float[6 * 4 * 2 * 3];
        for (var index = 0; index < table.Length; index += 3)
        {
            table[index + 1] = 1f;
            table[index + 2] = 1f;
        }
        var map = new DcpHueSatMap(6, 4, 2, false, table, null, 0);

        // The render pipeline applies HueSat to a CLONE of the base image;
        // clones are copy-on-write in Magick.
        using var clone = (MagickImage)image.Clone();
        DcpHueSatRenderer.Apply(clone, map);

        using var check = clone.GetPixels();
        foreach (var fraction in new[] { 0.1, 0.5, 0.6, 0.75, 0.95 })
        {
            var y = Math.Min(Height - 1, (int)(Height * fraction));
            var row = check.GetArea(0, y, Width, 1);
            double sum = 0;
            foreach (var value in row!) sum += value;
            Assert.True(
                sum > 0,
                $"Row at {fraction:P0} is entirely black after Apply.");
        }
    }
}

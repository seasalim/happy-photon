using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HistogramServiceTests
{
    [Fact]
    public void CalculateHistogram_ConsumesRenderResultWithEightBitBins()
    {
        using var result = CreateResult(
            width: 1,
            height: 1,
            [0x12AB, 0x34CD, 0x56EF]);
        var histogram = new HistogramData();

        new HistogramService().CalculateHistogram(result, histogram);

        Assert.Equal(1, histogram.Red[0x12]);
        Assert.Equal(1, histogram.Green[0x34]);
        Assert.Equal(1, histogram.Blue[0x56]);
        Assert.Equal(1, histogram.Luminance[45]);
        Assert.NotNull(histogram.Waveform);
    }

    [Fact]
    public void CalculateHistogram_DoesNotMutateOrDisposeRenderResult()
    {
        using var result = CreateResult(
            width: 2,
            height: 1,
            [
                0x0102, 0x0304, 0x0506,
                0xA1B2, 0xC3D4, 0xE5F6
            ]);
        var ownedImage = result.Image;
        var before = ReadPixels(ownedImage);

        new HistogramService().CalculateHistogram(result, new HistogramData());

        Assert.Same(ownedImage, result.Image);
        Assert.Equal((uint)2, result.Image.Width);
        Assert.Equal((uint)1, result.Image.Height);
        Assert.Equal(before, ReadPixels(result.Image));
    }

    [Fact]
    public void CalculateHistogram_DownscalesCloneToMaximumDimension()
    {
        const int width = 2048;
        const int height = 1024;
        using var result = CreateSolidResult(width, height, MagickColors.Red);
        var ownedImage = result.Image;
        var histogram = new HistogramData();

        new HistogramService().CalculateHistogram(result, histogram);

        Assert.Equal(1024 * 512, histogram.Red.Sum());
        Assert.Equal(1024 * 512, histogram.Green.Sum());
        Assert.Equal(1024 * 512, histogram.Blue.Sum());
        Assert.Equal((uint)width, result.Image.Width);
        Assert.Equal((uint)height, result.Image.Height);
        Assert.Same(ownedImage, result.Image);
    }

    [Fact]
    public void CalculateHistogram_PreservesGoldenBinBytes()
    {
        using var result = CreateResult(
            width: 4,
            height: 1,
            [
                0x0000, 0x0000, 0x0000,
                0x12AB, 0x34CD, 0x56EF,
                0xFFFF, 0x0000, 0x8000,
                0xFFFF, 0xFFFF, 0xFFFF
            ]);
        var histogram = new HistogramData();
        var golden = new HistogramData();
        AddGolden(golden, 0, 0, 0);
        AddGolden(golden, 0x12, 0x34, 0x56);
        AddGolden(golden, 0xFF, 0, 0x80);
        AddGolden(golden, 0xFF, 0xFF, 0xFF);

        new HistogramService().CalculateHistogram(result, histogram);

        Assert.Equal(GoldenBytes(golden), GoldenBytes(histogram));
    }

    private static byte[] GoldenBytes(HistogramData histogram)
    {
        var bytes = new byte[4 * 256 * sizeof(int)];
        Buffer.BlockCopy(histogram.Red, 0, bytes, 0, 256 * sizeof(int));
        Buffer.BlockCopy(histogram.Green, 0, bytes, 256 * sizeof(int), 256 * sizeof(int));
        Buffer.BlockCopy(histogram.Blue, 0, bytes, 512 * sizeof(int), 256 * sizeof(int));
        Buffer.BlockCopy(histogram.Luminance, 0, bytes, 768 * sizeof(int), 256 * sizeof(int));
        return bytes;
    }

    private static void AddGolden(HistogramData histogram, int r, int g, int b)
    {
        histogram.Red[r]++;
        histogram.Green[g]++;
        histogram.Blue[b]++;
        var luminance = Math.Clamp(
            (int)(0.299 * r + 0.587 * g + 0.114 * b),
            0,
            255);
        histogram.Luminance[luminance]++;
    }

    private static RenderResult CreateResult(
        int width,
        int height,
        ushort[] samples)
    {
        var image = new MagickImage(
            MagickColors.Black,
            (uint)width,
            (uint)height);
        using var pixels = image.GetPixels();
        for (var pixel = 0; pixel < samples.Length / 3; pixel++)
        {
            var offset = pixel * 3;
            pixels.SetPixel(
                pixel % width,
                pixel / width,
                samples.AsSpan(offset, 3).ToArray());
        }

        return new RenderResult(image, ClippingStats.Empty, null);
    }

    private static RenderResult CreateSolidResult(
        int width,
        int height,
        MagickColor color) =>
        new(
            new MagickImage(color, (uint)width, (uint)height),
            ClippingStats.Empty,
            null);

    private static ushort[] ReadPixels(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read histogram test pixels.");
}

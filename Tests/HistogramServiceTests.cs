using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using System.Runtime.InteropServices;
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

    [Theory]
    [InlineData(181, 91)]
    [InlineData(512, 512)]
    [InlineData(17, 13)]
    public void CalculateHistogram_ParallelPathMatchesSequentialReference(
        int width,
        int height)
    {
        var samples = CreateDeterministicSamples(width, height);
        using var result = CreateResult(width, height, samples);
        var actual = new HistogramData();

        new HistogramService().CalculateHistogram(result, actual);
        var expected = CalculateSequentialReference(samples, width, height);

        Assert.Equal(expected.Red, actual.Red);
        Assert.Equal(expected.Green, actual.Green);
        Assert.Equal(expected.Blue, actual.Blue);
        Assert.Equal(expected.Luminance, actual.Luminance);
        Assert.Equal(expected.MaxValue, actual.MaxValue);
        Assert.NotNull(actual.Waveform);
        Assert.Equal(
            expected.Waveform!.Luminance,
            actual.Waveform!.Luminance);
        Assert.Equal(
            expected.Waveform.ColumnSampleCounts,
            actual.Waveform.ColumnSampleCounts);
    }

    [Theory]
    [InlineData(17, 13, false)]
    [InlineData(511, 513, false)]
    [InlineData(512, 512, false)]
    [InlineData(521, 509, false)]
    [InlineData(251, 1049, false)]
    [InlineData(17, 13, true)]
    [InlineData(511, 513, true)]
    [InlineData(512, 512, true)]
    [InlineData(521, 509, true)]
    [InlineData(251, 1049, true)]
    public void CalculatePreviewHistogram_ParallelPathMatchesSequentialReference(
        int width,
        int height,
        bool includeWaveform)
    {
        var samples = CreateDeterministicSamples(width, height);
        var bgra = new byte[checked(width * height * 4)];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            bgra[pixel * 4] = (byte)(samples[pixel * 3 + 2] >> 8);
            bgra[pixel * 4 + 1] = (byte)(samples[pixel * 3 + 1] >> 8);
            bgra[pixel * 4 + 2] = (byte)(samples[pixel * 3] >> 8);
            bgra[pixel * 4 + 3] = byte.MaxValue;
        }
        var actual = new HistogramData();

        HistogramService.CalculatePreviewHistogram(
            bgra,
            width,
            height,
            actual,
            includeHistogram: true,
            includeWaveform);
        var expected = CalculateSequentialReference(samples, width, height);

        Assert.Equal(expected.Red, actual.Red);
        Assert.Equal(expected.Green, actual.Green);
        Assert.Equal(expected.Blue, actual.Blue);
        Assert.Equal(expected.Luminance, actual.Luminance);
        Assert.Equal(expected.MaxValue, actual.MaxValue);
        if (includeWaveform)
        {
            Assert.Equal(
                expected.Waveform!.Luminance,
                actual.Waveform!.Luminance);
            Assert.Equal(
                expected.Waveform.ColumnSampleCounts,
                actual.Waveform.ColumnSampleCounts);
        }
        else
        {
            Assert.Null(actual.Waveform);
        }
    }

    [Fact]
    public void CalculateHistogram_ParallelHistogramOnlyDoesNotCreateWaveform()
    {
        const int width = 512;
        const int height = 512;
        var samples = CreateDeterministicSamples(width, height);
        using var result = CreateResult(width, height, samples);
        var actual = new HistogramData();

        HistogramService.CalculateHistogramFromPreparedSnapshot(
            result.Image,
            actual,
            includeHistogram: true,
            includeWaveform: false);
        var expected = CalculateSequentialReference(samples, width, height);

        Assert.Equal(expected.Red, actual.Red);
        Assert.Equal(expected.Green, actual.Green);
        Assert.Equal(expected.Blue, actual.Blue);
        Assert.Equal(expected.Luminance, actual.Luminance);
        Assert.Equal(expected.MaxValue, actual.MaxValue);
        Assert.Null(actual.Waveform);
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

    private static HistogramData CalculateSequentialReference(
        ushort[] samples,
        int width,
        int height)
    {
        var histogram = new HistogramData();
        for (var offset = 0; offset < samples.Length; offset += 3)
        {
            AddGolden(
                histogram,
                samples[offset] >> 8,
                samples[offset + 1] >> 8,
                samples[offset + 2] >> 8);
        }
        histogram.Waveform = WaveformAccumulator.Accumulate(
            samples,
            width,
            height);
        histogram.Normalize();
        return histogram;
    }

    private static ushort[] CreateDeterministicSamples(int width, int height)
    {
        var samples = new ushort[checked(width * height * 3)];
        uint state = 0xC0FFEEu;
        for (var index = 0; index < samples.Length; index++)
        {
            state = state * 1664525u + 1013904223u;
            samples[index] = (ushort)(state >> 16);
        }
        return samples;
    }

    private static RenderResult CreateResult(
        int width,
        int height,
        ushort[] samples)
    {
        var settings = new PixelReadSettings(
            (uint)width,
            (uint)height,
            StorageType.Short,
            PixelMapping.RGB);
        settings.ReadSettings.ColorSpace = ColorSpace.sRGB;
        var image = new MagickImage(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            settings);

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

using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class HistogramServiceTests
{
    [Theory]
    [InlineData(17, 13, false, false)]
    [InlineData(511, 513, false, false)]
    [InlineData(512, 512, false, false)]
    [InlineData(521, 509, false, false)]
    [InlineData(251, 1049, false, false)]
    [InlineData(1600, 1067, false, false)]
    [InlineData(17, 13, true, false)]
    [InlineData(511, 513, true, false)]
    [InlineData(512, 512, true, false)]
    [InlineData(521, 509, true, false)]
    [InlineData(251, 1049, true, false)]
    [InlineData(1600, 1067, true, false)]
    [InlineData(17, 13, false, true)]
    [InlineData(511, 513, false, true)]
    [InlineData(512, 512, false, true)]
    [InlineData(521, 509, false, true)]
    [InlineData(251, 1049, false, true)]
    [InlineData(1600, 1067, false, true)]
    [InlineData(17, 13, true, true)]
    [InlineData(511, 513, true, true)]
    [InlineData(512, 512, true, true)]
    [InlineData(521, 509, true, true)]
    [InlineData(251, 1049, true, true)]
    [InlineData(1600, 1067, true, true)]
    public void CalculatePreviewHistogram_ParallelPathMatchesSequentialReference(
        int width,
        int height,
        bool includeWaveform,
        bool scaleWorkersWithFrame)
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
            includeWaveform,
            scaleWorkersWithFrame);
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
    public void Render_PreviewBgraIsIndependentOfStatisticsMode()
    {
        const int width = 17;
        const int height = 13;
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            CreateDeterministicSamples(width, height),
            height: height);
        var pipeline = new RenderPipeline();

        using var statisticsOff = RenderStatisticsMode(
            pipeline,
            baseImage,
            includeHistogram: false,
            includeWaveform: false);
        using var histogramOnly = RenderStatisticsMode(
            pipeline,
            baseImage,
            includeHistogram: true,
            includeWaveform: false);
        using var histogramAndWaveform = RenderStatisticsMode(
            pipeline,
            baseImage,
            includeHistogram: true,
            includeWaveform: true);

        Assert.Null(statisticsOff.Histogram);
        var histogramOnlyData = Assert.IsType<HistogramData>(
            histogramOnly.Histogram);
        var histogramAndWaveformData = Assert.IsType<HistogramData>(
            histogramAndWaveform.Histogram);
        Assert.Null(histogramOnlyData.Waveform);
        Assert.NotNull(histogramAndWaveformData.Waveform);
        var statisticsOffPixels = Assert.IsType<byte[]>(
            statisticsOff.PreviewPixels);
        Assert.Equal(statisticsOffPixels, histogramOnly.PreviewPixels);
        Assert.Equal(
            statisticsOffPixels,
            histogramAndWaveform.PreviewPixels);
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

    private static RenderResult RenderStatisticsMode(
        RenderPipeline pipeline,
        BaseImage baseImage,
        bool includeHistogram,
        bool includeWaveform) =>
        pipeline.Render(new RenderRequest(
            baseImage,
            new EditSettings(),
            RenderIntent.Preview,
            null,
            new RenderOptions(
                ComputeStats: false,
                ComputeHistogram: includeHistogram,
                ComputeWaveform: includeWaveform,
                PreparePreviewPixels: true)));

}

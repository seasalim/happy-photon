using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class RenderDedupHashControlTests
{
    // 720 x 540 exceeds 512 x 512, so the unscaled preview histogram also splits across workers.
    private const int HistogramWidth = 720;
    private const int HistogramHeight = 540;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewHistogram_MatchesFrozenHash(bool scaleWorkersWithFrame)
    {
        var bgra = Bgra(HistogramWidth, HistogramHeight);
        var histogram = new HistogramData();

        HistogramService.CalculatePreviewHistogram(
            bgra, HistogramWidth, HistogramHeight, histogram, true, scaleWorkersWithFrame);

        using var hash = new HashBuilder();
        AddHistogram(hash, histogram);
        AssertFrozen($"histogram-preview-{(scaleWorkersWithFrame ? "scaled" : "fixed")}", hash.Finish());
    }

    [Fact]
    public void ClippingAnalysis_MatchesFrozenHash()
    {
        using var image = CreateImage(Width, Height, 0x9E3779B9);
        var mask = SourceSaturationMask.CaptureEncoded(image, null, Width, CancellationToken.None);
        var projection = new SourceSaturationProjection(mask, new ChannelClip(0.01, 0.02, 0.03), 0.04);
        var frame = RenderColorEncoding.ConvertEncodedRec2020ToTarget(image, OutputColorSpace.Srgb);
        var bgra = new byte[Width * Height * 4];

        var analysis = ClippingStatsCalculator.Analyze(
            frame, Width, Height, projection, true, ClippingOverlaySide.Both, bgra);

        using var hash = new HashBuilder();
        var stats = analysis.Stats;

        foreach (var clip in new[] { stats.High, stats.Low })
        {
            hash.AddDouble(clip.R);
            hash.AddDouble(clip.G);
            hash.AddDouble(clip.B);
        }

        hash.AddDouble(stats.HighAny);
        hash.AddDouble(stats.LowAll);
        hash.AddInt32(stats.IsHighAvailable ? 1 : 0);
        hash.AddBytes(analysis.OverlayMask!.Flags);
        hash.AddBytes(bgra);
        analysis.OverlayMask.Dispose();
        AssertFrozen("clipping-analyze", hash.Finish());
    }

    [Theory]
    [InlineData("bayer")]
    [InlineData("xtrans")]
    public void RawSensorHistogram_MatchesFrozenHashAtEveryWorkerLimit(string pattern)
    {
        var frame = new HashControlRawFrame(600, 400, pattern == "xtrans");
        var histogramHashes = new List<string>();
        var artifactHashes = new List<string>();

        foreach (var limit in new int?[] { null, 1, 4 })
        {
            using var histogramHash = new HashBuilder();
            AddHistogram(histogramHash, RawSensorHistogram.Sample(frame, default, limit)!);
            histogramHashes.Add(histogramHash.Finish());
            var artifacts = RawSensorHistogram.SampleArtifacts(
                frame, CancellationToken.None, limit, 300, 200)!;
            using var artifactHash = new HashBuilder();
            AddHistogram(artifactHash, artifacts.Histogram);
            AddMask(artifactHash, artifacts.SourceSaturation!);
            artifactHashes.Add(artifactHash.Finish());
        }

        Assert.All(histogramHashes, hash => Assert.Equal(histogramHashes[0], hash));
        Assert.All(artifactHashes, hash => Assert.Equal(artifactHashes[0], hash));
        AssertFrozen($"raw-sensor-{pattern}-sample", histogramHashes[0]);
        AssertFrozen($"raw-sensor-{pattern}-artifacts", artifactHashes[0]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1023u)]
    public void SourceSaturationCapture_MatchesFrozenHash(uint? encodedMaximum)
    {
        using var image = CreateImage(Width, Height, 0x6A09E667);

        var mask = SourceSaturationMask.CaptureEncoded(
            image, encodedMaximum, 400, CancellationToken.None);

        using var hash = new HashBuilder();
        AddMask(hash, mask);
        AssertFrozen($"saturation-capture-{encodedMaximum?.ToString() ?? "q16"}", hash.Finish());
    }

    private static byte[] Bgra(int width, int height)
    {
        var samples = Samples(width, height, 0xBB67AE85);
        var bgra = new byte[width * height * 4];

        for (var pixel = 0; pixel < width * height; pixel++)
        {
            bgra[pixel * 4] = (byte)(samples[pixel * 3 + 2] >> 8);
            bgra[pixel * 4 + 1] = (byte)(samples[pixel * 3 + 1] >> 8);
            bgra[pixel * 4 + 2] = (byte)(samples[pixel * 3] >> 8);
            bgra[pixel * 4 + 3] = 255;
        }

        return bgra;
    }

    private static void AddHistogram(HashBuilder hash, HistogramData histogram)
    {
        hash.AddInt32((int)histogram.Domain);
        hash.AddInt32s(histogram.Red);
        hash.AddInt32s(histogram.Green);
        hash.AddInt32s(histogram.Blue);
        hash.AddInt32s(histogram.Luminance);
        hash.AddInt32(histogram.MaxValue);

        if (histogram.Clipping is { } clipping)
        {
            hash.AddInt64(clipping.Red);
            hash.AddInt64(clipping.Green);
            hash.AddInt64(clipping.Blue);
            hash.AddInt64(clipping.TotalVisibleSamples);
            hash.AddInt64(clipping.WhiteLevel);
        }

        if (histogram.Waveform is { } waveform)
        {
            hash.AddUInt16s(waveform.Luminance);
            hash.AddUInt16s(waveform.ColumnSampleCounts);
        }
    }

    private sealed class HashControlRawFrame : IRawSensorFrame
    {
        private readonly ushort[] _samples;

        internal HashControlRawFrame(int width, int height, bool xtrans)
        {
            VisibleWidth = (uint)width;
            VisibleHeight = (uint)height;
            RawPitch = (uint)((width + 2) * 2);
            Filters = xtrans ? 9u : 0xB4B4B4B4;
            XTrans = Enumerable.Range(0, 36).Select(index => (sbyte)((index * 7 + index / 6) % 3)).ToArray();
            _samples = new ushort[(height + 1) * (width + 2)];
            var state = 0x510E527Fu;

            for (var index = 0; index < _samples.Length; index++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                _samples[index] = (ushort)(state % 4200);
            }
        }

        public int Colors => 3;

        public uint Filters { get; }

        public IReadOnlyList<sbyte> XTrans { get; }

        public uint RawPitch { get; }

        public uint VisibleWidth { get; }

        public uint VisibleHeight { get; }

        public uint TopMargin => 1;

        public uint LeftMargin => 2;

        public uint Black => 64;

        public uint Maximum => 4095;

        public uint RepeatingRows => 0;

        public uint RepeatingColumns => 0;

        public IReadOnlyList<uint> CBlack { get; } = new uint[4104];

        public Span<ushort> Samples => _samples;
    }
}

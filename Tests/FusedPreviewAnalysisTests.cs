using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FusedPreviewAnalysisTests
{
    private static readonly (RenderIntent Intent, OutputColorSpace Space)[] Targets =
    [
        (RenderIntent.Preview, OutputColorSpace.Srgb),
        (RenderIntent.Preview, OutputColorSpace.DisplayP3),
        (RenderIntent.Export, OutputColorSpace.Srgb),
        (RenderIntent.Export, OutputColorSpace.DisplayP3)
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryQuantum_FusedPassMatchesMagick(bool alpha)
    {
        using var image = new MagickImage(MagickColors.Black, 256, 256)
        {
            ColorType = alpha ? ColorType.TrueColorAlpha : ColorType.TrueColor,
            Depth = 16
        };
        using (var pixels = image.GetPixels())
        {
            var layout = RenderKernelSupport.GetLayout(pixels);
            var a = pixels.GetChannelIndex(PixelChannel.Alpha);
            var samples = new ushort[65536 * layout.Channels];
            for (var q = 0; q <= ushort.MaxValue; q++)
            {
                var offset = q * layout.Channels;
                samples[offset + layout.Red] = (ushort)q;
                samples[offset + layout.Green] = (ushort)((q + 1) & 65535);
                samples[offset + layout.Blue] = (ushort)((q + 128) & 65535);
                if (a is { } index) samples[offset + (int)index] = (ushort)(65535 - q);
            }
            pixels.SetArea(0, 0, image.Width, image.Height, samples);
        }

        var source = new SourceSaturationProjection(CreateMask(256, 256),
            new ChannelClip(0.25, 0.5, 0.75), 0.875);
        using var area = image.GetPixels();
        var frame = new RenderColorEncoding.EncodedFrame(
            area.GetArea(0, 0, image.Width, image.Height)!,
            RenderKernelSupport.GetLayout(area),
            area.GetChannelIndex(PixelChannel.Alpha) is { } channel ? (int)channel : null);
        var original = frame.Samples.ToArray();
        foreach (var options in Options())
        {
            var analyze = options.ComputeStats || options.ComputeOverlayMasks &&
                options.OverlaySides != ClippingOverlaySide.None;
            var bgra = options.PreparePreviewPixels || options.ComputeHistogram || options.ComputeWaveform
                ? new byte[256 * 256 * 4] : null;
            var actual = ClippingStatsCalculator.Analyze(frame, 256, 256,
                analyze ? source : null, options.ComputeOverlayMasks, options.OverlaySides, bgra, analyze);
            using var mask = actual.OverlayMask;
            var expected = PreviewAnalysisOracle.Analyze(image, source,
                options.ComputeOverlayMasks, options.OverlaySides);
            using var expectedMask = expected.OverlayMask;
            Assert.Equal(analyze ? expected.Stats : ClippingStats.Empty, actual.Stats);
            AssertMask(expectedMask, mask);
            if (bgra != null) Assert.Equal(BitmapConversionService.CopyBgraPixels(image), bgra);
        }
        Assert.Equal(original, frame.Samples);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Render_AllOptionsMatchMagickWithAndWithoutSourceArtifact(bool alpha)
    {
        ushort[] edges = [0, 1, 127, 128, 129, 256, 257, 32767, 32768, 65406, 65407, 65535];
        var samples = Enumerable.Range(0, 32 * 8 * 3).Select(i => edges[i % edges.Length]).ToArray();
        using var source = RenderPipelineTestSupport.CreateBase(samples, height: 8);
        if (alpha) AddAlpha(source.Pixels);
        foreach (var includeSource in new[] { false, true })
            AssertAllOptions(source, includeSource ? CreateMask(32, 8) : null);
    }

    [Theory]
    [InlineData("canon-eos-6d-iso-6400.cr2", false)]
    [InlineData("canon-eos-6d-iso-6400.cr2", true)]
    [InlineData("fujifilm-x30.raf", false)]
    [InlineData("fujifilm-x30.raf", true)]
    [InlineData("generated-jpeg", false)]
    [InlineData("generated-jpeg", true)]
    public void Fixtures_MatchMagickAtPreviewSizeAndAcrossAllOptions(string fixture, bool alpha)
    {
        using var temporary = new TemporaryDirectory();
        var path = fixture == "generated-jpeg"
            ? CullPerfFiles.GeneratedJpeg(temporary.Path) : GoldenTestPaths.Asset(fixture);
        var availability = new SourceAvailabilityService();
        Assert.True(SourceAccessPolicy.CanRead(availability.GetAvailability(path), SourceReadIntent.Background));
        var loader = new GatedBaseImageLoader(
            new BaseLoaderRouter(new RawBaseLoader(), new StandardBaseLoader()), availability);
        using var pair = loader.LoadPreviewBaseWithOutcome(new ImageFile(path), BaseDecodeSettings.Default,
            CancellationToken.None).Pair!;
        using var source = new BaseImage(new MagickImage(pair.Interactive.Pixels), pair.Interactive.Info);
        if (alpha) AddAlpha(source.Pixels);
        foreach (var target in Targets)
        {
            var request = Request(source, target, new RenderOptions(true, true,
                ClippingOverlaySide.Both, true, true, true), null);
            AssertRender(request);
        }

        // Exercise the complete option product on bounded owned copies of each real fixture.
        using var smallPixels = new MagickImage(source.Pixels);
        smallPixels.Resize(64, 64);
        using var small = new BaseImage(new MagickImage(smallPixels), source.Info);
        AssertAllOptions(small, CreateMask((int)small.Pixels.Width, (int)small.Pixels.Height));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Finalizer_OnlyRetainsRequestedFrameWithoutChangingImage(bool retain)
    {
        using var source = new MagickImage(MagickColors.Gray, 32, 16);
        using var expected = RenderFinalizer.Finalize(source, 16, OutputColorSpace.DisplayP3,
            OutputSharpeningMode.Off, false);
        using var actual = RenderFinalizer.FinalizeOwned(new MagickImage(source), 16,
            OutputColorSpace.DisplayP3, OutputSharpeningMode.Off, false, retain, out var frame);
        Assert.Equal(retain, frame.HasValue);
        Assert.Equal(ColorSpace.sRGB, actual.ColorSpace);
        Assert.Equal(RenderPipelineTestSupport.ReadPixels(expected), RenderPipelineTestSupport.ReadPixels(actual));
        if (frame is { } encoded)
        {
            using var pixels = actual.GetPixels();
            Assert.Equal(pixels.GetArea(0, 0, actual.Width, actual.Height), encoded.Samples);
        }
    }

    [Fact]
    public void Finalizer_RefusesToRetainFrameForWatermarkedRender()
    {
        var owned = new MagickImage(MagickColors.Gray, 32, 16);
        Assert.Throws<ArgumentException>(() => RenderFinalizer.FinalizeOwned(owned, 16,
            OutputColorSpace.Srgb, OutputSharpeningMode.Off, false, true, out _,
            watermark: new WatermarkSpec("x")));
        Assert.Throws<ObjectDisposedException>(() => owned.Width);
    }

    private static void AssertAllOptions(BaseImage source, SourceSaturationMask? mask)
    {
        foreach (var target in Targets)
        foreach (var options in Options())
            AssertRender(Request(source, target, options, mask));
    }

    private static RenderRequest Request(BaseImage source,
        (RenderIntent Intent, OutputColorSpace Space) target, RenderOptions options,
        SourceSaturationMask? mask) =>
        new(source, new EditSettings
        {
            Contrast = 20,
            Rotation = 90,
            Crop = new CropRegion { Left = 0.1, Top = 0, Right = 1, Bottom = 0.9 },
            Detail = new DetailSettings { CaptureSharpen = 0 }
        }, target.Intent, 1600, options, target.Space) { SourceSaturation = mask };

    private static void AssertRender(RenderRequest request)
    {
        using var actual = new RenderPipeline().Render(request);
        Assert.Equal(ColorSpace.sRGB, actual.Image.ColorSpace);
        var width = checked((int)actual.Image.Width);
        var height = checked((int)actual.Image.Height);
        var createOverlay = request.Intent == RenderIntent.Preview &&
            request.Options.ComputeOverlayMasks && request.Options.OverlaySides != ClippingOverlaySide.None;
        var analyze = request.Options.ComputeStats || createOverlay;
        using var geometry = RenderGeometry.Apply(request.Base.Pixels, request.Settings, out var trace);
        var projection = SourceSaturationMaskProjector.Project(
            request.SourceSaturation, request.Settings, trace, width, height);
        var expected = PreviewAnalysisOracle.Analyze(actual.Image, projection,
            createOverlay, request.Options.OverlaySides);
        using var expectedMask = expected.OverlayMask;
        Assert.Equal(analyze ? expected.Stats : ClippingStats.Empty, actual.Clipping);
        AssertMask(expectedMask, actual.OverlayMask);
        var bgra = BitmapConversionService.CopyBgraPixels(actual.Image);
        if (request.Options.PreparePreviewPixels) Assert.Equal(bgra, actual.PreviewPixels);
        else Assert.Null(actual.PreviewPixels);
        if (request.Options.ComputeHistogram || request.Options.ComputeWaveform)
        {
            var histogram = new HistogramData();
            HistogramService.CalculatePreviewHistogram(bgra, width, height, histogram,
                request.Options.ComputeWaveform);
            AssertHistogram(histogram, Assert.IsType<HistogramData>(actual.Histogram));
        }
        else Assert.Null(actual.Histogram);
    }

    private static IEnumerable<RenderOptions> Options()
    {
        for (var sides = 0; sides < 4; sides++)
        for (var bits = 0; bits < 32; bits++)
            yield return new RenderOptions((bits & 1) != 0, (bits & 2) != 0,
                (ClippingOverlaySide)sides, (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0);
    }

    private static void AssertMask(ClippingMask? expected, ClippingMask? actual)
    {
        if (expected == null) { Assert.Null(actual); return; }
        Assert.NotNull(actual);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(expected.Sides, actual.Sides);
        Assert.Equal(expected.Flags.ToArray(), actual.Flags.ToArray());
    }

    private static void AssertHistogram(HistogramData expected, HistogramData actual)
    {
        Assert.Equal(expected.Red, actual.Red);
        Assert.Equal(expected.Green, actual.Green);
        Assert.Equal(expected.Blue, actual.Blue);
        Assert.Equal(expected.Luminance, actual.Luminance);
        Assert.Equal(expected.MaxValue, actual.MaxValue);
        if (expected.Waveform == null) Assert.Null(actual.Waveform);
        else
        {
            Assert.NotNull(actual.Waveform);
            Assert.Equal(expected.Waveform.Luminance, actual.Waveform.Luminance);
            Assert.Equal(expected.Waveform.ColumnSampleCounts, actual.Waveform.ColumnSampleCounts);
        }
    }

    private static SourceSaturationMask CreateMask(int width, int height)
    {
        var mask = new SourceSaturationMask(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            mask.SetFlags(x, y, (byte)((x + y) % 8));
        return mask;
    }

    private static void AddAlpha(MagickImage image)
    {
        image.Alpha(AlphaOption.Set);
        using var pixels = image.GetPixels();
        var samples = pixels.GetArea(0, 0, image.Width, image.Height)!;
        var channels = checked((int)pixels.Channels);
        var alpha = checked((int)pixels.GetChannelIndex(PixelChannel.Alpha)!.Value);
        for (var p = 0; p < samples.Length / channels; p++)
            samples[p * channels + alpha] = (ushort)((p * 257) & 65535);
        pixels.SetArea(0, 0, image.Width, image.Height, samples);
    }
}

using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ClippingStatsTests
{
    [Fact]
    public void Analyze_UsesPinnedDisplayThresholdSemantics()
    {
        using var image = CreateImage(
        [
            65535, 1000, 1000,
            65535, 65535, 65535,
            0, 0, 0,
            0, 1000, 65535
        ]);

        var result = ClippingStatsCalculator.Analyze(
            image,
            rawNearClip: 0.125,
            createOverlay: true);
        using var overlay = result.OverlayMask;

        Assert.Equal(new ChannelClip(0.5, 0.25, 0.5), result.Stats.High);
        Assert.Equal(new ChannelClip(0.5, 0.25, 0.25), result.Stats.Low);
        Assert.Equal(0.75, result.Stats.HighAny);
        Assert.Equal(0.25, result.Stats.LowAll);
        Assert.Equal(0.125, result.Stats.RawNearClip);
        Assert.NotNull(overlay);
        Assert.Equal((int)image.Width, overlay.Width);
        Assert.Equal((int)image.Height, overlay.Height);
        Assert.Equal(
            new[]
            {
                (byte)ClippingOverlaySide.Highlights,
                (byte)ClippingOverlaySide.Highlights,
                (byte)ClippingOverlaySide.DisplayFloor,
                (byte)ClippingOverlaySide.Highlights
            },
            overlay.Flags.ToArray());
    }

    [Fact]
    public void CalculateRawNearClip_UsesBasePixelsOnlyForRawSources()
    {
        using var raw = RenderPipelineTestSupport.CreateBase(
            DisplayToWorking(
            [
                64000, 0, 0,
                65535, 0, 0,
                0, 65535, 0,
                0, 0, 1
            ]),
            isRaw: true);
        using var standard = RenderPipelineTestSupport.CreateBase(
        [
            65535, 65535, 65535
        ], isRaw: false);

        Assert.Equal(0.5, ClippingStatsCalculator.CalculateRawNearClip(raw));
        Assert.Equal(0, ClippingStatsCalculator.CalculateRawNearClip(standard));
    }

    private static ushort[] DisplayToWorking(ushort[] samples)
    {
        var result = new ushort[samples.Length];
        var matrix = RgbColorSpaceMatrices.LinearSrgbToLinearRec2020;
        for (var offset = 0; offset < samples.Length; offset += 3)
        {
            var red = samples[offset] / (double)ushort.MaxValue;
            var green = samples[offset + 1] / (double)ushort.MaxValue;
            var blue = samples[offset + 2] / (double)ushort.MaxValue;
            for (var row = 0; row < 3; row++)
            {
                result[offset + row] = (ushort)Math.Round(Math.Clamp(
                    matrix[row, 0] * red + matrix[row, 1] * green +
                    matrix[row, 2] * blue,
                    0,
                    1) * ushort.MaxValue);
            }
        }

        return result;
    }

    [Fact]
    public void Analyze_PinsInclusiveEightBitThresholdBoundary()
    {
        // Pin the locked inclusive 253/255 Q16 boundary exactly.
        using var image = CreateImage(
        [
            65020, 65021, 65020,
            128, 129, 128
        ]);

        var result = ClippingStatsCalculator.Analyze(
            image,
            rawNearClip: 0,
            createOverlay: false);

        Assert.Equal(new ChannelClip(0, 0.5, 0), result.Stats.High);
        Assert.Equal(new ChannelClip(0.5, 0, 0.5), result.Stats.Low);
        Assert.Equal(0.5, result.Stats.HighAny);
        Assert.Equal(0, result.Stats.LowAll);
    }

    [Fact]
    public void ParallelMaskWrites_MatchSequentialReferenceAcrossChunkBoundaries()
    {
        // 16,471 pixels: two unequal chunks, prime count. Flags are written
        // inside the parallel loop; verify against a sequential re-derivation.
        const int pixels = 16471;
        var random = new Random(83);
        var samples = new ushort[pixels * 3];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = checked((ushort)random.Next(0, 65536));
        }

        var expected = new byte[pixels];
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            var offset = pixel * 3;
            var anyHigh = samples[offset] >=
                    ClippingStatsCalculator.HighThreshold ||
                samples[offset + 1] >=
                    ClippingStatsCalculator.HighThreshold ||
                samples[offset + 2] >=
                    ClippingStatsCalculator.HighThreshold;
            var allLow = samples[offset] <= 128 &&
                samples[offset + 1] <= 128 &&
                samples[offset + 2] <= 128;
            expected[pixel] = anyHigh
                ? (byte)ClippingOverlaySide.Highlights
                : allLow
                    ? (byte)ClippingOverlaySide.DisplayFloor
                    : (byte)0;
        }

        using var image = CreateImage(samples);
        var result = ClippingStatsCalculator.Analyze(
            image,
            rawNearClip: 0,
            createOverlay: true);
        using var mask = result.OverlayMask;

        Assert.Equal(expected, mask!.Flags.ToArray());
    }

    [Fact]
    public void Analyze_FiltersSemanticMaskByRequestedSide()
    {
        using var image = CreateImage(
        [
            ushort.MaxValue, ushort.MaxValue, ushort.MaxValue,
            0, 0, 0
        ]);

        var highlights = ClippingStatsCalculator.Analyze(
            image,
            rawNearClip: 0,
            createOverlay: true,
            overlaySides: ClippingOverlaySide.Highlights);
        var floor = ClippingStatsCalculator.Analyze(
            image,
            rawNearClip: 0,
            createOverlay: true,
            overlaySides: ClippingOverlaySide.DisplayFloor);
        using var highlightMask = highlights.OverlayMask;
        using var floorMask = floor.OverlayMask;

        Assert.Equal(
            [(byte)ClippingOverlaySide.Highlights, 0],
            highlightMask!.Flags.ToArray());
        Assert.Equal(
            [0, (byte)ClippingOverlaySide.DisplayFloor],
            floorMask!.Flags.ToArray());
        Assert.Equal(ClippingOverlaySide.Highlights, highlightMask.Sides);
        Assert.Equal(ClippingOverlaySide.DisplayFloor, floorMask.Sides);
    }

    [Fact]
    public void Render_RawShoulderedHighlightUsesFinalOutput()
    {
        // A synthetic RAW base avoids decode variance while proving that the
        // overlay follows AgX output instead of pre-crossing scene values.
        using var raw = RenderPipelineTestSupport.CreateBase(
            [ushort.MaxValue, 0, 0],
            isRaw: true);
        using var result = new RenderPipeline().Render(new RenderRequest(
            raw,
            new EditSettings
            {
                Detail = new DetailSettings { CaptureSharpen = 0 }
            },
            RenderIntent.Preview,
            null,
            new RenderOptions(true, true)));
        using var pushed = new RenderPipeline().Render(new RenderRequest(
            raw,
            new EditSettings
            {
                Exposure = 3,
                Highlights = 100,
                Detail = new DetailSettings { CaptureSharpen = 0 }
            },
            RenderIntent.Preview,
            null,
            new RenderOptions(true, true)));

        Assert.Equal(ChannelClip.Empty, result.Clipping.High);
        Assert.Equal(0, result.Clipping.HighAny);
        Assert.All(
            RenderPipelineTestSupport.ReadPixels(result.Image),
            value => Assert.True(value < ClippingStatsCalculator.HighThreshold));
        Assert.Equal(0, result.OverlayMask!.Flags[0]);
        Assert.Equal(1, pushed.Clipping.HighAny);
        Assert.Equal(
            (byte)ClippingOverlaySide.Highlights,
            pushed.OverlayMask!.Flags[0]);
    }

    [Fact]
    public void Render_StandardHighlightsRespondToExposureInBothDirections()
    {
        // Output-referred warnings must clear and return with visible edits.
        using var white = RenderPipelineTestSupport.CreateBase(
            [ushort.MaxValue, ushort.MaxValue, ushort.MaxValue]);
        using var clipped = RenderWithOverlay(white, new EditSettings());
        using var recovered = RenderWithOverlay(
            white,
            new EditSettings { Exposure = -3 });
        using var clippedAgain = RenderWithOverlay(white, new EditSettings());

        Assert.Equal(1, clipped.Clipping.HighAny);
        Assert.Equal(
            (byte)ClippingOverlaySide.Highlights,
            clipped.OverlayMask!.Flags[0]);
        Assert.Equal(0, recovered.Clipping.HighAny);
        Assert.Equal(0, recovered.OverlayMask!.Flags[0]);
        Assert.Equal(1, clippedAgain.Clipping.HighAny);
        Assert.Equal(
            (byte)ClippingOverlaySide.Highlights,
            clippedAgain.OverlayMask!.Flags[0]);
    }

    private static RenderResult RenderWithOverlay(
        BaseImage image,
        EditSettings settings)
    {
        settings.Detail = new DetailSettings { CaptureSharpen = 0 };
        return new RenderPipeline().Render(new RenderRequest(
            image,
            settings,
            RenderIntent.Preview,
            null,
            new RenderOptions(true, true)));
    }

    [Fact]
    public void Analyze_EightBitOriginHighlightsFlagSolidly()
    {
        // Every 8-bit code from the locked boundary through white must flag.
        Assert.Equal(253 * 257, ClippingStatsCalculator.HighThreshold);
        using var image = CreateImage(
        [
            253 * 257, 20000, 20000,
            254 * 257, 20000, 20000,
            255 * 257, 20000, 20000
        ]);

        var result = ClippingStatsCalculator.Analyze(
            image,
            rawNearClip: 0,
            createOverlay: true,
            overlaySides: ClippingOverlaySide.Highlights);
        using var mask = result.OverlayMask;

        Assert.Equal(1, result.Stats.HighAny);
        Assert.All(mask!.Flags.ToArray(), flag => Assert.Equal(
            (byte)ClippingOverlaySide.Highlights,
            flag));
    }

    [Fact]
    public void ParallelAnalyses_MatchSequentialReferenceAcrossChunkBoundaries()
    {
        // 16,471 pixels: two unequal chunks in every parallelized analysis
        // loop, prime so no worker count divides it evenly. The reference
        // counts below re-derive the pinned thresholds sequentially.
        const int pixels = 16471;
        var random = new Random(271);
        var samples = new ushort[pixels * 3];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = checked((ushort)random.Next(0, 65536));
        }

        long highR = 0, highG = 0, highB = 0;
        long lowR = 0, lowG = 0, lowB = 0;
        long highAny = 0, lowAll = 0;
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            var offset = pixel * 3;
            var rHigh = samples[offset] >=
                ClippingStatsCalculator.HighThreshold;
            var gHigh = samples[offset + 1] >=
                ClippingStatsCalculator.HighThreshold;
            var bHigh = samples[offset + 2] >=
                ClippingStatsCalculator.HighThreshold;
            var rLow = samples[offset] <= 128;
            var gLow = samples[offset + 1] <= 128;
            var bLow = samples[offset + 2] <= 128;
            if (rHigh) highR++;
            if (gHigh) highG++;
            if (bHigh) highB++;
            if (rLow) lowR++;
            if (gLow) lowG++;
            if (bLow) lowB++;
            if (rHigh || gHigh || bHigh) highAny++;
            else if (rLow && gLow && bLow) lowAll++;
        }

        using var image = CreateImage(samples);
        var result = ClippingStatsCalculator.Analyze(
            image,
            rawNearClip: 0,
            createOverlay: false);

        Assert.Equal(
            new ChannelClip(
                highR / (double)pixels,
                highG / (double)pixels,
                highB / (double)pixels),
            result.Stats.High);
        Assert.Equal(
            new ChannelClip(
                lowR / (double)pixels,
                lowG / (double)pixels,
                lowB / (double)pixels),
            result.Stats.Low);
        Assert.Equal(highAny / (double)pixels, result.Stats.HighAny);
        Assert.Equal(lowAll / (double)pixels, result.Stats.LowAll);

        using var raw = RenderPipelineTestSupport.CreateBase(
            samples,
            isRaw: true);
        long nearClip = 0;
        var matrix = RgbColorSpaceMatrices.LinearRec2020ToLinearSrgb;
        var threshold = 64880 / (double)ushort.MaxValue;
        for (var pixel = 0; pixel < pixels; pixel++)
        {
            var offset = pixel * 3;
            var red = samples[offset] / (double)ushort.MaxValue;
            var green = samples[offset + 1] / (double)ushort.MaxValue;
            var blue = samples[offset + 2] / (double)ushort.MaxValue;
            var clipped = false;
            for (var row = 0; row < 3 && !clipped; row++)
            {
                clipped = matrix[row, 0] * red + matrix[row, 1] * green +
                    matrix[row, 2] * blue >= threshold;
            }
            if (clipped) nearClip++;
        }

        Assert.Equal(
            nearClip / (double)pixels,
            ClippingStatsCalculator.CalculateRawNearClip(raw));
    }

    private static MagickImage CreateImage(ushort[] samples)
    {
        var settings = new PixelReadSettings(
            (uint)(samples.Length / 3),
            1,
            StorageType.Short,
            PixelMapping.RGB);
        settings.ReadSettings.ColorSpace = ColorSpace.sRGB;
        return new MagickImage(
            MemoryMarshal.AsBytes(samples.AsSpan()),
            settings);
    }
}

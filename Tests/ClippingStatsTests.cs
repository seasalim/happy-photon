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
        Assert.Equal(image.Width, overlay.Width);
        Assert.Equal(image.Height, overlay.Height);
        var mask = overlay.GetPixelsUnsafe().ToShortArray(PixelMapping.RGBA) ??
            throw new InvalidOperationException("Unable to read overlay pixels.");
        Assert.Equal(ushort.MaxValue, mask[0]);
        Assert.Equal((ushort)0, mask[1]);
        Assert.Equal((ushort)0, mask[2]);
        Assert.Equal((ushort)24576, mask[3]);
        Assert.Equal((ushort)0, mask[8]);
        Assert.Equal((ushort)0, mask[9]);
        Assert.Equal(ushort.MaxValue, mask[10]);
        Assert.Equal((ushort)24576, mask[11]);
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
    public void Analyze_PinsHalfEightBitThresholdBoundaries()
    {
        using var image = CreateImage(
        [
            65406, 65407, 65406,
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
    public void Render_RawHighlightsUsePreInsetSceneWhite()
    {
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

        Assert.Equal(new ChannelClip(1, 0, 0), result.Clipping.High);
        Assert.Equal(1, result.Clipping.HighAny);
        Assert.All(
            RenderPipelineTestSupport.ReadPixels(result.Image),
            value => Assert.True(value < 65407));
        var mask = result.OverlayMask!.GetPixelsUnsafe()
            .ToShortArray(PixelMapping.RGBA)!;
        Assert.Equal(ushort.MaxValue, mask[0]);
        Assert.Equal((ushort)0, mask[1]);
        Assert.Equal((ushort)0, mask[2]);
    }

    [Fact]
    public void Render_RawHighlightsRespondToExposureAndWhiteBalance()
    {
        using var halfGrey = RenderPipelineTestSupport.CreateBase(
            [32768, 32768, 32768],
            isRaw: true);
        using var neutral = RenderRaw(halfGrey, new EditSettings());
        using var exposed = RenderRaw(
            halfGrey,
            new EditSettings { Exposure = 1 });

        Assert.Equal(ChannelClip.Empty, neutral.Clipping.High);
        Assert.Equal(0, neutral.Clipping.HighAny);
        Assert.Equal(new ChannelClip(1, 1, 1), exposed.Clipping.High);
        Assert.Equal(1, exposed.Clipping.HighAny);

        using var wbBase = RenderPipelineTestSupport.CreateBase(
            [40000, 40000, 40000],
            isRaw: true);
        using var whiteBalanced = RenderRaw(
            wbBase,
            new EditSettings
            {
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Picked,
                    Gains = [2, 1, 1]
                }
            });
        Assert.Equal(new ChannelClip(1, 0, 0), whiteBalanced.Clipping.High);
        Assert.Equal(1, whiteBalanced.Clipping.HighAny);
    }

    private static RenderResult RenderRaw(
        BaseImage image,
        EditSettings settings)
    {
        settings.Detail = new DetailSettings { CaptureSharpen = 0 };
        return new RenderPipeline().Render(new RenderRequest(
            image,
            settings,
            RenderIntent.Preview,
            null,
            new RenderOptions(true, false)));
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

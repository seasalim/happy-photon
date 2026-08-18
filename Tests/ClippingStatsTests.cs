using System.Runtime.InteropServices;
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

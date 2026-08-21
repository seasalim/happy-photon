using System.Runtime.InteropServices;
using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ClippingStatsTests
{
    [Fact]
    public void Analyze_MergesIndependentSourceHighAndDisplayFloorBits()
    {
        using var image = CreateImage([0, 0, 0]);
        var mask = Mask(1, 1, (0, 0, (byte)1));
        var source = new SourceSaturationProjection(
            mask,
            new ChannelClip(1, 0, 0),
            1);

        var analysis = ClippingStatsCalculator.Analyze(
            image,
            source,
            createOverlay: true);
        using var overlay = analysis.OverlayMask;

        Assert.True(analysis.Stats.IsHighAvailable);
        Assert.Equal(new ChannelClip(1, 0, 0), analysis.Stats.High);
        Assert.Equal(1, analysis.Stats.HighAny);
        Assert.Equal(new ChannelClip(1, 1, 1), analysis.Stats.Low);
        Assert.Equal(1, analysis.Stats.LowAll);
        Assert.Equal(
            (byte)ClippingOverlaySide.Both,
            overlay!.Flags[0]);
    }

    [Fact]
    public void Analyze_MissingArtifactKeepsFloorAndNeverFallsBackToOutputHigh()
    {
        using var image = CreateImage(
        [
            ushort.MaxValue, ushort.MaxValue, ushort.MaxValue,
            0, 0, 0
        ]);

        var analysis = ClippingStatsCalculator.Analyze(
            image,
            sourceSaturation: null,
            createOverlay: true);
        using var overlay = analysis.OverlayMask;

        Assert.False(analysis.Stats.IsHighAvailable);
        Assert.Equal(ChannelClip.Empty, analysis.Stats.High);
        Assert.Equal(0, analysis.Stats.HighAny);
        Assert.Equal(0.5, analysis.Stats.LowAll);
        Assert.Equal(
            [0, (byte)ClippingOverlaySide.DisplayFloor],
            overlay!.Flags.ToArray());
    }

    [Fact]
    public void Render_SourceHighIsEditInvariantWhileFloorResponds()
    {
        var mask = Mask(2, 1, (0, 0, (byte)1));
        using var source = RenderPipelineTestSupport.CreateBase(
        [
            50, 50, 50,
            50, 50, 50
        ], sourceSaturation: mask);
        using var neutral = Render(source, new EditSettings());
        using var darkened = Render(source, new EditSettings
        {
            Exposure = -3,
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = 9000,
                Tint = 50
            },
            Effects = new EffectsSettings { Grain = 35 }
        });

        Assert.Equal(neutral.Clipping.High, darkened.Clipping.High);
        Assert.Equal(neutral.Clipping.HighAny, darkened.Clipping.HighAny);
        Assert.True(neutral.Clipping.IsHighAvailable);
        Assert.True(darkened.Clipping.IsHighAvailable);
        Assert.Equal(
            neutral.OverlayMask!.Flags.ToArray().Select(HighBit),
            darkened.OverlayMask!.Flags.ToArray().Select(HighBit));
        Assert.NotEqual(neutral.Clipping.LowAll, darkened.Clipping.LowAll);
    }

    [Fact]
    public void Project_RotateCropAndDownscaleKeepFlagsAligned()
    {
        var mask = Mask(4, 3, (1, 1, (byte)4));
        using var source = RenderPipelineTestSupport.CreateBase(
            Enumerable.Repeat((ushort)1000, 4 * 3 * 3).ToArray(),
            height: 3,
            sourceSaturation: mask);
        var settings = new EditSettings
        {
            Rotation = 90,
            Crop = new CropRegion
            {
                Left = 0.25,
                Top = 0,
                Right = 1,
                Bottom = 1
            }
        };
        using var geometryImage = new MagickImage(MagickColors.Black, 4, 3);
        var trace = RenderGeometry.Apply(geometryImage, settings);

        var projection = SourceSaturationMaskProjector.Project(
            source,
            settings,
            trace,
            targetWidth: 1,
            targetHeight: 2);

        Assert.NotNull(projection);
        Assert.Equal(4, projection!.Mask.GetFlags(0, 0));
        Assert.Equal(0, projection.Mask.GetFlags(0, 1));
        Assert.Equal(new ChannelClip(0, 0, 0.5), projection.High);
        Assert.Equal(0.5, projection.HighAny);
    }

    [Fact]
    public void SourceMask_OrientationAndResizeKeepFlagsAligned()
    {
        var source = Mask(3, 2, (0, 0, (byte)1));

        var oriented = source.OrientAndResize(
            orientation: 6,
            targetWidth: 4,
            targetHeight: 6);

        Assert.Equal(1, oriented.GetFlags(2, 0));
        Assert.Equal(1, oriented.GetFlags(3, 1));
        Assert.Equal(0, oriented.GetFlags(0, 0));
        Assert.Equal(0, oriented.GetFlags(3, 2));
    }

    [Fact]
    public void Project_HorizonAndResizePreserveAnIsolatedFlagAndCacheGeometry()
    {
        var mask = Mask(20, 10, (10, 5, (byte)2));
        using var source = RenderPipelineTestSupport.CreateBase(
            Enumerable.Repeat((ushort)1000, 20 * 10 * 3).ToArray(),
            height: 10,
            sourceSaturation: mask);
        var settings = new EditSettings { HorizonRotation = 7.5 };
        using var geometryImage = new MagickImage(MagickColors.Black, 20, 10);
        var trace = RenderGeometry.Apply(geometryImage, settings);
        var first = SourceSaturationMaskProjector.Project(
            source,
            settings,
            trace,
            5,
            2);
        var tonalEdit = settings.Clone();
        tonalEdit.Exposure = 2;
        tonalEdit.Highlights = -80;
        tonalEdit.Effects = new EffectsSettings { Vignette = -50 };
        var second = SourceSaturationMaskProjector.Project(
            source,
            tonalEdit,
            trace,
            5,
            2);

        Assert.Same(first, second);
        Assert.True(first!.HighAny > 0);
        Assert.InRange(first.HighAny, 0.1, 0.2);
    }

    [Fact]
    public void Analyze_FiltersOverlayWithoutChangingStatistics()
    {
        using var image = CreateImage([0, 0, 0]);
        var source = new SourceSaturationProjection(
            Mask(1, 1, (0, 0, (byte)7)),
            new ChannelClip(1, 1, 1),
            1);

        var highlights = ClippingStatsCalculator.Analyze(
            image,
            source,
            true,
            ClippingOverlaySide.Highlights);
        var floor = ClippingStatsCalculator.Analyze(
            image,
            source,
            true,
            ClippingOverlaySide.DisplayFloor);
        using var highlightMask = highlights.OverlayMask;
        using var floorMask = floor.OverlayMask;

        Assert.Equal(
            (byte)ClippingOverlaySide.Highlights,
            highlightMask!.Flags[0]);
        Assert.Equal(
            (byte)ClippingOverlaySide.DisplayFloor,
            floorMask!.Flags[0]);
        Assert.Equal(highlights.Stats, floor.Stats);
    }

    private static byte HighBit(byte value) =>
        (byte)(value & (byte)ClippingOverlaySide.Highlights);

    private static RenderResult Render(BaseImage image, EditSettings settings)
    {
        settings.Detail = new DetailSettings { CaptureSharpen = 0 };
        return new RenderPipeline().Render(new RenderRequest(
            image,
            settings,
            RenderIntent.Preview,
            null,
            new RenderOptions(true, true)));
    }

    private static SourceSaturationMask Mask(
        int width,
        int height,
        params (int X, int Y, byte Flags)[] values)
    {
        var result = new SourceSaturationMask(width, height);
        foreach (var value in values)
        {
            result.SetFlags(value.X, value.Y, value.Flags);
        }
        return result;
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

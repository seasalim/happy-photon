using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderEffectsTests
{
    [Fact]
    public void InactiveSettings_SkipWithoutChangingPixels()
    {
        using var source = CreatePattern(19, 13);
        var baseline = ReadRgba(source);

        RenderEffects.Apply(source, new EffectsSettings
        {
            Midpoint = 82,
            GrainSize = GrainSize.Coarse
        });

        Assert.Equal(baseline, ReadRgba(source));
    }

    [Fact]
    public void InactiveSettings_ReturnBeforePixelAccess()
    {
        var disposed = CreatePattern(3, 2);
        disposed.Dispose();

        var exception = Record.Exception(() => RenderEffects.Apply(
            disposed,
            new EffectsSettings
            {
                Midpoint = 90,
                GrainSize = GrainSize.Coarse
            }));

        Assert.Null(exception);
    }

    [Fact]
    public void Grain_IsDeterministicMonochromeAndPreservesAlpha()
    {
        using var first = CreatePattern(31, 17, includeAlpha: true);
        using var second = new MagickImage(first);
        var before = ReadRgba(first);
        var settings = new EffectsSettings
        {
            Grain = 63,
            GrainSize = GrainSize.Coarse
        };

        RenderEffects.Apply(first, settings);
        RenderEffects.Apply(second, settings.Clone());
        var actual = ReadRgba(first);

        Assert.Equal(actual, ReadRgba(second));
        for (var offset = 0; offset < actual.Length; offset += 4)
        {
            var redDelta = actual[offset] - before[offset];
            Assert.Equal(redDelta, actual[offset + 1] - before[offset + 1]);
            Assert.Equal(redDelta, actual[offset + 2] - before[offset + 2]);
            Assert.Equal(before[offset + 3], actual[offset + 3]);
        }
    }

    [Fact]
    public void Grain_UsesStablePatternWhenAmountChanges()
    {
        using var low = CreateSolid(29, 11, 30000, 31000, 32000);
        using var high = new MagickImage(low);
        var baseline = ReadRgb(low);

        RenderEffects.Apply(low, new EffectsSettings
        {
            Grain = 20,
            GrainSize = GrainSize.Fine
        });
        RenderEffects.Apply(high, new EffectsSettings
        {
            Grain = 40,
            GrainSize = GrainSize.Fine
        });
        var lowPixels = ReadRgb(low);
        var highPixels = ReadRgb(high);

        for (var offset = 0; offset < baseline.Length; offset += 3)
        {
            var lowDelta = lowPixels[offset] - baseline[offset];
            var highDelta = highPixels[offset] - baseline[offset];
            Assert.Equal(Math.Sign(lowDelta), Math.Sign(highDelta));
            Assert.InRange(Math.Abs(highDelta - lowDelta * 2), 0, 1);
        }
    }

    [Fact]
    public void GrainSizes_MapToPinnedCellScales()
    {
        Assert.NotEqual(
            RenderEffects.GrainSample(0, 0, GrainSize.Fine),
            RenderEffects.GrainSample(1, 0, GrainSize.Fine));

        var mediumLeft = RenderEffects.GrainSample(0, 0, GrainSize.Medium);
        var mediumRight = RenderEffects.GrainSample(2, 0, GrainSize.Medium);
        Assert.Equal(
            (mediumLeft + mediumRight) / 2,
            RenderEffects.GrainSample(1, 0, GrainSize.Medium),
            12);

        var coarseLeft = RenderEffects.GrainSample(0, 0, GrainSize.Coarse);
        var coarseRight = RenderEffects.GrainSample(3, 0, GrainSize.Coarse);
        Assert.Equal(
            coarseLeft + (coarseRight - coarseLeft) / 3,
            RenderEffects.GrainSample(1, 0, GrainSize.Coarse),
            12);
        Assert.Equal(
            coarseLeft + (coarseRight - coarseLeft) * 2 / 3,
            RenderEffects.GrainSample(2, 0, GrainSize.Coarse),
            12);
    }

    [Fact]
    public void Grain_GamutClampPreservesChannelDifferencesAtBoundaries()
    {
        using var image = CreateBoundarySamples();
        var before = ReadRgb(image);

        RenderEffects.Apply(image, new EffectsSettings
        {
            Grain = 100,
            GrainSize = GrainSize.Fine
        });
        var after = ReadRgb(image);

        for (var offset = 0; offset < after.Length; offset += 3)
        {
            Assert.Equal(
                before[offset + 1] - before[offset],
                after[offset + 1] - after[offset]);
            Assert.Equal(
                before[offset + 2] - before[offset + 1],
                after[offset + 2] - after[offset + 1]);
        }
    }

    [Fact]
    public void Vignette_HasPinnedSignMidpointAndNormalizedFrameBehavior()
    {
        var negative = RenderEffects.VignetteStrength(
            0, 0, 101, 75, -60, 50);
        var positive = RenderEffects.VignetteStrength(
            0, 0, 101, 75, 60, 50);
        var laterOnset = RenderEffects.VignetteStrength(
            0, 0, 101, 75, -60, 80);
        var center = RenderEffects.VignetteStrength(
            50, 37, 101, 75, -60, 50);
        var small = RenderEffects.VignetteStrength(
            2, 4, 9, 15, -60, 50);
        var large = RenderEffects.VignetteStrength(
            7, 13, 27, 45, -60, 50);

        Assert.True(negative < 0);
        Assert.True(positive > 0);
        Assert.True(Math.Abs(laterOnset) < Math.Abs(negative));
        Assert.Equal(0, center);
        Assert.Equal(small, large, 12);
    }

    [Fact]
    public void Vignette_DarkensOrLiftsCornersAndIsEdgeWeighted()
    {
        using var dark = CreateSolid(41, 31, 20000, 30000, 40000);
        using var light = new MagickImage(dark);

        RenderEffects.Apply(dark, new EffectsSettings { Vignette = -70 });
        RenderEffects.Apply(light, new EffectsSettings { Vignette = 70 });
        var darkPixels = ReadRgb(dark);
        var lightPixels = ReadRgb(light);
        var center = ((15 * 41) + 20) * 3;

        Assert.True(darkPixels[0] < 20000);
        Assert.True(lightPixels[0] > 20000);
        Assert.Equal(20000, darkPixels[center]);
        Assert.Equal(20000, lightPixels[center]);
    }

    [Fact]
    public void CombinedEffects_ApplyVignetteBeforeGrain()
    {
        var combinedSettings = new EffectsSettings
        {
            Vignette = -75,
            Midpoint = 35,
            Grain = 80,
            GrainSize = GrainSize.Medium
        };
        using var combined = CreatePattern(23, 17);
        using var expected = new MagickImage(combined);
        using var reversed = new MagickImage(combined);

        RenderEffects.Apply(combined, combinedSettings);
        RenderEffects.Apply(expected, new EffectsSettings
        {
            Vignette = combinedSettings.Vignette,
            Midpoint = combinedSettings.Midpoint
        });
        RenderEffects.Apply(expected, new EffectsSettings
        {
            Grain = combinedSettings.Grain,
            GrainSize = combinedSettings.GrainSize
        });
        RenderEffects.Apply(reversed, new EffectsSettings
        {
            Grain = combinedSettings.Grain,
            GrainSize = combinedSettings.GrainSize
        });
        RenderEffects.Apply(reversed, new EffectsSettings
        {
            Vignette = combinedSettings.Vignette,
            Midpoint = combinedSettings.Midpoint
        });

        Assert.Equal(ReadRgb(expected), ReadRgb(combined));
        Assert.NotEqual(ReadRgb(reversed), ReadRgb(combined));
    }

    [Fact]
    public void Pipeline_ExplicitInactiveEffectsAreBitIdenticalToNull()
    {
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            CreateBaseSamples(37, 19),
            height: 19);
        var pipeline = new RenderPipeline();
        using var baseline = pipeline.Render(CreateRequest(
            baseImage,
            new EditSettings()));
        using var explicitIdentity = pipeline.Render(CreateRequest(
            baseImage,
            new EditSettings
            {
                Effects = new EffectsSettings
                {
                    Midpoint = 91,
                    GrainSize = GrainSize.Coarse
                }
            }));

        Assert.Equal(
            ReadRgb(baseline.Image),
            ReadRgb(explicitIdentity.Image));
    }

    [Fact]
    public void CommittedOffCenterCrop_RecentersVignetteOnOutputFrame()
    {
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            Enumerable.Repeat((ushort)32000, 12 * 8 * 3).ToArray(),
            height: 8);
        var settings = new EditSettings
        {
            Crop = new CropRegion
            {
                Left = 0,
                Top = 0,
                Right = 0.5,
                Bottom = 1
            },
            Effects = new EffectsSettings { Vignette = -80 }
        };

        using var render = new RenderPipeline().Render(
            CreateRequest(baseImage, settings));
        var pixels = ReadRgb(render.Image);
        var width = checked((int)render.Image.Width);
        var height = checked((int)render.Image.Height);

        Assert.Equal(pixels[0], pixels[(width - 1) * 3]);
        Assert.Equal(
            pixels[((height - 1) * width) * 3],
            pixels[(height * width - 1) * 3]);
        Assert.True(pixels[((height / 2 * width) + width / 2) * 3] > pixels[0]);
    }

    private static RenderRequest CreateRequest(
        BaseImage baseImage,
        EditSettings settings) =>
        new(
            baseImage,
            settings,
            RenderIntent.Export,
            null,
            new RenderOptions(false, false));

    private static MagickImage CreatePattern(
        int width,
        int height,
        bool includeAlpha = false)
    {
        var image = CreateSolid(width, height, 12000, 24000, 36000);
        if (includeAlpha)
        {
            image.Alpha(AlphaOption.Set);
        }
        using var pixels = image.GetPixels();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var red = (ushort)(10000 + x * 600);
                var green = (ushort)(red + 5000);
                var blue = (ushort)(red + 10000);
                if (includeAlpha)
                {
                    pixels.SetPixel(x, y,
                        [red, green, blue, (ushort)(1000 + x * 100)]);
                }
                else
                {
                    pixels.SetPixel(x, y, [red, green, blue]);
                }
            }
        }
        return image;
    }

    private static MagickImage CreateSolid(
        int width,
        int height,
        ushort red,
        ushort green,
        ushort blue)
    {
        var image = new MagickImage(MagickColors.Black, (uint)width, (uint)height)
        {
            Depth = 16,
            ColorSpace = ColorSpace.sRGB
        };
        using var pixels = image.GetPixels();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels.SetPixel(x, y, [red, green, blue]);
            }
        }
        return image;
    }

    private static MagickImage CreateBoundarySamples()
    {
        var image = CreateSolid(3, 1, 0, 10000, 20000);
        using var pixels = image.GetPixels();
        pixels.SetPixel(1, 0, [45535, 55535, 65535]);
        pixels.SetPixel(2, 0, [0, 32768, 65535]);
        return image;
    }

    private static ushort[] CreateBaseSamples(int width, int height)
    {
        var values = new ushort[width * height * 3];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = (ushort)(4000 + index * 37 % 56000);
        }
        return values;
    }

    private static ushort[] ReadRgb(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGB) ??
        throw new InvalidOperationException("Unable to read RGB pixels.");

    private static ushort[] ReadRgba(MagickImage image) =>
        image.GetPixelsUnsafe().ToShortArray(PixelMapping.RGBA) ??
        throw new InvalidOperationException("Unable to read RGBA pixels.");
}

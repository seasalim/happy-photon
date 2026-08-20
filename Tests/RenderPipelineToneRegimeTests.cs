using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderPipelineToneRegimeTests
{
    private readonly RenderPipeline _pipeline = new();

    [Fact]
    public void RawBrightnessAndBaseLookAreDormant()
    {
        using var baseImage = RenderPipelineTestSupport.CreateBase(
        [
            8000, 16000, 24000,
            32000, 40000, 48000
        ], isRaw: true);
        using var baseline = Render(new EditSettings(), baseImage);
        var expected = RenderPipelineTestSupport.ReadPixels(baseline.Image);

        foreach (var brightness in new[] { -100, 0, 100 })
        foreach (var baseLook in new bool?[] { null, false, true })
        {
            using var actual = Render(
                new EditSettings
                {
                    Brightness = brightness,
                    BaseLook = baseLook
                },
                baseImage);
            Assert.Equal(
                expected,
                RenderPipelineTestSupport.ReadPixels(actual.Image));
        }
    }

    [Fact]
    public void StandardBrightnessAndBaseLookRemainActive()
    {
        ushort[] samples = [4000, 8000, 12000, 24000, 32000, 48000];
        using var standard = RenderPipelineTestSupport.CreateBase(samples);

        using var baseline = Render(new EditSettings(), standard);
        using var bright = Render(
            new EditSettings { Brightness = 100 },
            standard);
        using var looked = Render(
            new EditSettings { BaseLook = true },
            standard);

        Assert.NotEqual(
            RenderPipelineTestSupport.ReadPixels(baseline.Image),
            RenderPipelineTestSupport.ReadPixels(bright.Image));
        Assert.NotEqual(
            RenderPipelineTestSupport.ReadPixels(baseline.Image),
            RenderPipelineTestSupport.ReadPixels(looked.Image));
    }

    [Fact]
    public void RawSharedStageMatchesIsolatedCrossing()
    {
        ushort[] samples =
        [
            4000, 8000, 12000,
            16000, 24000, 32000,
            48000, 36000, 20000
        ];
        using var raw = RenderPipelineTestSupport.CreateBase(samples, isRaw: true);
        var settings = new EditSettings
        {
            Exposure = 0.75,
            Contrast = 25,
            Highlights = -50,
            Shadows = 35,
            Detail = new DetailSettings { CaptureSharpen = 0 }
        };
        var expected = samples.ToArray();
        new AgxCrossing(
            new AgxToneParameters(
                settings.Exposure,
                raw.Info.SourceExposureBiasEv,
                settings.Contrast,
                settings.Highlights,
                settings.Shadows,
                settings.Curve),
            ChromaticAdaptation.Identity()).Apply(expected);

        using var actual = RenderShared(settings, raw);

        Assert.Equal(expected, RenderPipelineTestSupport.ReadPixels(actual));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RedChannelCurveChangesRedOutputInBothToneRegimes(bool isRaw)
    {
        ushort[] samples =
        [
            12000, 12000, 12000,
            28000, 28000, 28000,
            48000, 48000, 48000
        ];
        using var baseImage = RenderPipelineTestSupport.CreateBase(
            samples,
            isRaw: isRaw);
        using var baseline = RenderShared(new EditSettings(), baseImage);
        var redCurve = new CurveData();
        redCurve.AddPointAndReturnIndex(0.5, 0.75);
        using var curved = RenderShared(
            new EditSettings { CurveRed = redCurve },
            baseImage);
        var baselinePixels = RenderPipelineTestSupport.ReadPixels(baseline);
        var curvedPixels = RenderPipelineTestSupport.ReadPixels(curved);

        Assert.NotEqual(baselinePixels[3], curvedPixels[3]);
        if (!isRaw)
        {
            Assert.Equal(baselinePixels[4], curvedPixels[4]);
            Assert.Equal(baselinePixels[5], curvedPixels[5]);
        }
    }

    private RenderResult Render(EditSettings settings, BaseImage baseImage) =>
        _pipeline.Render(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            null,
            new RenderOptions()));

    private MagickImage RenderShared(
        EditSettings settings,
        BaseImage baseImage) =>
        _pipeline.RenderDisplayRec2020(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            null,
            new RenderOptions()));
}

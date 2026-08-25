using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderPipelineNoiseReductionTests
{
    [Fact]
    public void Render_AppliesLuminanceNoiseReductionBeforeCaptureSharpen()
    {
        using var baseImage = CreateDetailPatternBase();
        var settings = new EditSettings
        {
            Saturation = 20,
            Detail = new DetailSettings
            {
                LuminanceNr = 70,
                CaptureSharpen = 100
            }
        };
        var pipeline = new RenderPipeline();
        using var upstream = RenderShared(
            pipeline,
            new EditSettings { Saturation = 20 },
            baseImage);
        using var expected = new MagickImage(upstream);
        RenderNoiseReduction.Apply(expected, baseImage.Info, settings.Detail);
        RenderSharpening.ApplyCapture(expected, baseImage.Info, settings.Detail);
        using var reversed = new MagickImage(upstream);
        RenderSharpening.ApplyCapture(reversed, baseImage.Info, settings.Detail);
        RenderNoiseReduction.Apply(reversed, baseImage.Info, settings.Detail);

        using var actual = RenderShared(pipeline, settings, baseImage);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(expected),
            RenderPipelineTestSupport.ReadPixels(actual));
        Assert.NotEqual(
            RenderPipelineTestSupport.ReadPixels(reversed),
            RenderPipelineTestSupport.ReadPixels(actual));
    }

    private static MagickImage RenderShared(
        RenderPipeline pipeline,
        EditSettings settings,
        BaseImage baseImage) =>
        pipeline.RenderDisplayRec2020(new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            null,
            new RenderOptions()));

    private static BaseImage CreateDetailPatternBase()
    {
        const int width = 64;
        const int height = 48;
        var random = new Random(195);
        var samples = new ushort[width * height * 3];
        for (var index = 0; index < samples.Length; index += 3)
        {
            var luma = random.Next(8000, 57000);
            samples[index] = checked((ushort)luma);
            samples[index + 1] = checked((ushort)Math.Clamp(
                luma + 1200,
                0,
                ushort.MaxValue));
            samples[index + 2] = checked((ushort)Math.Clamp(
                luma - 900,
                0,
                ushort.MaxValue));
        }
        return RenderPipelineTestSupport.CreateBase(samples, height: height);
    }
}

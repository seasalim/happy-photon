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
        RenderSharpening.ApplyCapture(
            expected, baseImage.Info, settings.Detail, RenderIntent.Preview);
        using var reversed = new MagickImage(upstream);
        RenderSharpening.ApplyCapture(
            reversed, baseImage.Info, settings.Detail, RenderIntent.Preview);
        RenderNoiseReduction.Apply(reversed, baseImage.Info, settings.Detail);

        using var actual = RenderShared(pipeline, settings, baseImage);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(expected),
            RenderPipelineTestSupport.ReadPixels(actual));
        Assert.NotEqual(
            RenderPipelineTestSupport.ReadPixels(reversed),
            RenderPipelineTestSupport.ReadPixels(actual));
    }

    [Fact]
    public void SharedRender_AppliesChromaNoiseReductionBeforeCaptureSharpen()
    {
        using var baseImage = CreateChromaDetailPatternBase();
        var settings = CreateChromaAndSharpenSettings();
        var pipeline = new RenderPipeline();
        using var upstream = RenderShared(
            pipeline,
            new EditSettings { Saturation = 20 },
            baseImage);
        using var expected = new MagickImage(upstream);
        RenderNoiseReduction.Apply(expected, baseImage.Info, settings.Detail);
        RenderSharpening.ApplyCapture(
            expected, baseImage.Info, settings.Detail, RenderIntent.Preview);
        using var reversed = new MagickImage(upstream);
        RenderSharpening.ApplyCapture(
            reversed, baseImage.Info, settings.Detail, RenderIntent.Preview);
        RenderNoiseReduction.Apply(reversed, baseImage.Info, settings.Detail);

        using var actual = RenderShared(pipeline, settings, baseImage);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(expected),
            RenderPipelineTestSupport.ReadPixels(actual));
        Assert.NotEqual(
            RenderPipelineTestSupport.ReadPixels(reversed),
            RenderPipelineTestSupport.ReadPixels(actual));
    }

    [Fact]
    public void RestingRender_AppliesChromaNoiseReductionBeforeCaptureSharpen()
    {
        using var baseImage = CreateChromaDetailPatternBase();
        var settings = CreateChromaAndSharpenSettings();
        var pipeline = new RenderPipeline();
        using var upstream = RenderShared(
            pipeline,
            new EditSettings { Saturation = 20 },
            baseImage);
        using var expectedDisplay = new MagickImage(upstream);
        RenderNoiseReduction.Apply(
            expectedDisplay,
            baseImage.Info,
            settings.Detail);
        RenderSharpening.ApplyCapture(
            expectedDisplay,
            baseImage.Info,
            settings.Detail,
            RenderIntent.Preview);
        using var expected = RenderFinalizer.Finalize(
            expectedDisplay,
            maxDimension: null,
            OutputColorSpace.Srgb,
            OutputSharpeningMode.Off,
            wasResized: false);
        using var reversedDisplay = new MagickImage(upstream);
        RenderSharpening.ApplyCapture(
            reversedDisplay,
            baseImage.Info,
            settings.Detail,
            RenderIntent.Preview);
        RenderNoiseReduction.Apply(
            reversedDisplay,
            baseImage.Info,
            settings.Detail);
        using var reversed = RenderFinalizer.Finalize(
            reversedDisplay,
            maxDimension: null,
            OutputColorSpace.Srgb,
            OutputSharpeningMode.Off,
            wasResized: false);
        var request = new RenderRequest(
            baseImage,
            settings,
            RenderIntent.Preview,
            MaxDimension: null,
            new RenderOptions(false, false));

        using var actual = pipeline.RenderResting(
            request,
            RenderExecutionOptions.Resting(CancellationToken.None, 2));

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(expected),
            RenderPipelineTestSupport.ReadPixels(actual.Image));
        Assert.NotEqual(
            RenderPipelineTestSupport.ReadPixels(reversed),
            RenderPipelineTestSupport.ReadPixels(actual.Image));
    }

    [Fact]
    public void MonochromeRender_SkipsChromaNoiseReduction()
    {
        using var baseImage = CreateDetailPatternBase(isMonochrome: true);
        var pipeline = new RenderPipeline();

        using var neutral = RenderShared(pipeline, new EditSettings(), baseImage);
        using var active = RenderShared(
            pipeline,
            new EditSettings
            {
                Detail = new DetailSettings { ChromaNr = 100 }
            },
            baseImage);

        Assert.Equal(
            RenderPipelineTestSupport.ReadPixels(neutral),
            RenderPipelineTestSupport.ReadPixels(active));
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

    private static EditSettings CreateChromaAndSharpenSettings() => new()
    {
        Saturation = 20,
        Detail = new DetailSettings
        {
            ChromaNr = 70,
            CaptureSharpen = 100
        }
    };

    private static BaseImage CreateDetailPatternBase(
        bool isMonochrome = false)
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
        return RenderPipelineTestSupport.CreateBase(
            samples,
            height: height,
            isMonochrome: isMonochrome);
    }

    private static BaseImage CreateChromaDetailPatternBase()
    {
        const int width = 64;
        const int height = 48;
        var random = new Random(225);
        var samples = new ushort[width * height * 3];
        for (var index = 0; index < samples.Length; index += 3)
        {
            var luma = random.Next(12000, 53000);
            var red = luma + random.Next(-5000, 5001);
            var blue = luma + random.Next(-5000, 5001);
            var green = (luma - Rec2020Luminance.Red * red -
                Rec2020Luminance.Blue * blue) / Rec2020Luminance.Green;
            samples[index] = ToQuantum(red);
            samples[index + 1] = ToQuantum(green);
            samples[index + 2] = ToQuantum(blue);
        }
        return RenderPipelineTestSupport.CreateBase(samples, height: height);
    }

    private static ushort ToQuantum(double value) =>
        (ushort)Math.Clamp(Math.Round(value), 0, ushort.MaxValue);
}

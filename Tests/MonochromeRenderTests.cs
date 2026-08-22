using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;
using static HappyPhoton.Tests.RenderPipelineTestSupport;

namespace HappyPhoton.Tests;

public sealed class MonochromeRenderTests
{
    [Theory]
    [InlineData(RenderIntent.Preview, OutputColorSpace.Srgb)]
    [InlineData(RenderIntent.Export, OutputColorSpace.Srgb)]
    [InlineData(RenderIntent.Export, OutputColorSpace.DisplayP3)]
    public void ColorSettings_AreDormantAndOutputRemainsExactlyNeutral(
        RenderIntent intent,
        OutputColorSpace outputColorSpace)
    {
        using var baseImage = CreateMonochromeBase();
        var pipeline = new RenderPipeline();
        var baselineSettings = ToneSettings();
        var extremeSettings = baselineSettings.Clone();
        extremeSettings.Wb = new WhiteBalanceSettings
        {
            Mode = WbMode.Custom,
            Kelvin = 12000,
            Tint = 100
        };
        extremeSettings.Saturation = 100;
        extremeSettings.Vibrance = 100;
        extremeSettings.Mixer = ExtremeMixer();
        extremeSettings.CurveRed = Curve(0.35, 0.95);
        extremeSettings.CurveGreen = Curve(0.5, 0.05);
        extremeSettings.CurveBlue = Curve(0.7, 0.9);

        using var baseline = pipeline.Render(Request(
            baseImage,
            baselineSettings,
            intent,
            outputColorSpace));
        using var extreme = pipeline.Render(Request(
            baseImage,
            extremeSettings,
            intent,
            outputColorSpace));
        var baselinePixels = ReadPixels(baseline.Image);
        var extremePixels = ReadPixels(extreme.Image);

        Assert.Equal(baselinePixels, extremePixels);
        AssertNeutral(extremePixels);
    }

    [Fact]
    public void ChromaStage_IsNotEnteredForMonochrome()
    {
        var pipeline = new RenderPipeline();
        var settings = ToneSettings();
        settings.Wb = new WhiteBalanceSettings
        {
            Mode = WbMode.Custom,
            Kelvin = 6500,
            Tint = 0
        };
        settings.Saturation = 100;
        settings.Vibrance = 100;
        settings.Mixer = ExtremeMixer();

        using var mono = CreateMonochromeBase();
        var monoStages = new List<string>();
        using (pipeline.RenderResting(
            Request(mono, settings, RenderIntent.Preview, OutputColorSpace.Srgb),
            RenderExecutionOptions.Resting(
                CancellationToken.None,
                stageStarted: monoStages.Add)))
        {
        }
        Assert.DoesNotContain("chroma", monoStages);
        Assert.Contains("finalization", monoStages);

        using var color = CreateColorBase();
        var colorStages = new List<string>();
        using (pipeline.RenderResting(
            Request(color, settings, RenderIntent.Preview, OutputColorSpace.Srgb),
            RenderExecutionOptions.Resting(
                CancellationToken.None,
                stageStarted: colorStages.Add)))
        {
        }
        Assert.Contains("chroma", colorStages);
    }

    private static BaseImage CreateColorBase()
    {
        var samples = new ushort[48 * 32 * 3];
        for (var sample = 0; sample < samples.Length; sample++)
        {
            samples[sample] = (ushort)(500 + sample % 199 * 300);
        }
        return CreateBase(samples, isRaw: true, height: 32);
    }

    private static BaseImage CreateMonochromeBase()
    {
        var samples = new ushort[48 * 32 * 3];
        for (var pixel = 0; pixel < samples.Length / 3; pixel++)
        {
            var gray = (ushort)(1000 + pixel % 64 * 900);
            samples[pixel * 3] = gray;
            samples[pixel * 3 + 1] = gray;
            samples[pixel * 3 + 2] = gray;
        }
        var table = Enumerable.Repeat(new[] { 120f, 2f, 0.5f }, 4)
            .SelectMany(value => value)
            .ToArray();
        return CreateBase(
            samples,
            isRaw: true,
            height: 32,
            hueSatMap: new DcpHueSatMap(2, 2, 1, false, table, null, 0),
            isMonochrome: true);
    }

    private static EditSettings ToneSettings() => new()
    {
        Exposure = 0.75,
        Contrast = 35,
        Highlights = -40,
        Shadows = 25,
        Curve = Curve(0.45, 0.58)
    };

    private static CurveData Curve(double x, double y)
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(x, y);
        return curve;
    }

    private static ColorMixerSettings ExtremeMixer()
    {
        var mixer = new ColorMixerSettings();
        foreach (var band in Enum.GetValues<ColorMixerBand>())
        {
            var settings = mixer.GetBand(band);
            settings.Hue = 100;
            settings.Saturation = -100;
            settings.Luminance = 100;
        }
        return mixer;
    }

    private static RenderRequest Request(
        BaseImage baseImage,
        EditSettings settings,
        RenderIntent intent,
        OutputColorSpace outputColorSpace) => new(
            baseImage,
            settings,
            intent,
            MaxDimension: null,
            new RenderOptions(false, false),
            outputColorSpace);

    private static void AssertNeutral(IReadOnlyList<ushort> pixels)
    {
        for (var offset = 0; offset < pixels.Count; offset += 3)
        {
            Assert.Equal(pixels[offset], pixels[offset + 1]);
            Assert.Equal(pixels[offset], pixels[offset + 2]);
        }
    }
}

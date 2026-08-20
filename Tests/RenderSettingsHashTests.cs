using System.Globalization;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderSettingsHashTests
{
    [Fact]
    public void Compute_IsStableAcrossCloneAndCulture()
    {
        var settings = CreateSettings();
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fa-IR");
            var first = RenderSettingsHash.Compute(settings);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var second = RenderSettingsHash.Compute(settings.Clone());

            Assert.Equal(first, second);
            Assert.Equal(64, first.Length);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Compute_ChangesWithPixelsOrPipelineIdentity()
    {
        var settings = CreateSettings();
        var baseline = RenderSettingsHash.Compute(settings);
        var changed = settings.Clone();
        changed.Exposure += 0.25;

        Assert.NotEqual(baseline, RenderSettingsHash.Compute(changed));
        Assert.NotEqual(
            baseline,
            RenderSettingsHash.Compute(
                settings,
                RenderPipeline.Version + 1,
                BaseImage.Version));
        Assert.NotEqual(
            baseline,
            RenderSettingsHash.Compute(
                settings,
                RenderPipeline.Version,
                BaseImage.Version + 1));
    }

    [Fact]
    public void Compute_DefaultUsesActiveRenderPipelineIdentity()
    {
        var settings = CreateSettings();

        Assert.Equal(
            RenderSettingsHash.Compute(settings),
            RenderSettingsHash.Compute(
                settings,
                RenderPipeline.Version,
                BaseImage.Version));
        Assert.NotEqual(
            RenderSettingsHash.Compute(settings),
            RenderSettingsHash.Compute(
                settings,
                RenderPipeline.Version + 1,
                BaseImage.Version));
    }

    [Fact]
    public void Compute_ChangesForEveryV2SettingsGroup()
    {
        var baseline = RenderSettingsHash.Compute(new EditSettings());
        var whiteBalance = new EditSettings
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = 6500,
                Tint = 10
            }
        };
        var detail = new EditSettings
        {
            Detail = new DetailSettings
            {
                CaptureSharpen = 20,
                NoiseReduction = FbddMode.Light,
                ChromaNr = 10
            }
        };

        Assert.NotEqual(baseline, RenderSettingsHash.Compute(whiteBalance));
        Assert.NotEqual(baseline, RenderSettingsHash.Compute(detail));
        Assert.NotEqual(baseline, RenderSettingsHash.Compute(
            new EditSettings { BaseLook = true }));
        Assert.NotEqual(baseline, RenderSettingsHash.Compute(
            new EditSettings { HlReconstruction = HlReconstructionMode.Blend }));
        Assert.NotEqual(baseline, RenderSettingsHash.Compute(
            new EditSettings { CurveRed = CreateCurve() }));
    }

    private static CurveData CreateCurve()
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(0.5, 0.7);
        return curve;
    }

    [Fact]
    public void Compute_ProfileOutcomeTokenSeparatesSuccessFromFallback()
    {
        var settings = new EditSettings
        {
            RawProfile = new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = "synthetic.dcp",
                ContentHash = new string('c', 64)
            }
        };

        var success = RenderSettingsHash.Compute(settings, "user:success");
        var rejected = RenderSettingsHash.Compute(
            settings,
            "user:success:hash-mismatch:replacement");

        Assert.NotEqual(success, rejected);
        Assert.Equal(
            RenderSettingsHash.Compute(new EditSettings()),
            RenderSettingsHash.Compute(new EditSettings(), string.Empty));
    }

    private static EditSettings CreateSettings() => new()
    {
        Exposure = 1.25,
        Wb = new WhiteBalanceSettings
        {
            Mode = WbMode.Custom,
            Kelvin = 6200,
            Tint = -18
        },
        Brightness = 9,
        Contrast = 11,
        Saturation = 13,
        Vibrance = 15,
        Shadows = 17,
        Highlights = -19,
        Rotation = 90,
        HorizonRotation = 1.75,
        Crop = new CropRegion
        {
            Left = 0.1,
            Top = 0.2,
            Right = 0.8,
            Bottom = 0.9
        },
        Curve = new CurveData
        {
            Points =
            [
                new CurvePoint(0, 0),
                new CurvePoint(0.4, 0.3),
                new CurvePoint(1, 1)
            ]
        },
        AppliedPresetId = "preset-a"
    };
}

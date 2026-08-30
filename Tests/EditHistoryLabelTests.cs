using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditHistoryLabelTests
{
    [Fact]
    public void ExposureIncludesNewValueAndDelta()
    {
        Assert.Equal("Exposure +0.30 (+0.30)", EditHistoryLabel.Derive(
            new EditSettings(), new EditSettings { Exposure = .3 }));
    }

    [Fact]
    public void NestedSingleControlsIncludeValueAndDelta()
    {
        AssertLabel("Sharpen +25 (+25)", after => after.Detail.CaptureSharpen = 25);
        AssertLabel("Luma NR +14 (+14)", after => after.Detail.LuminanceNr = 14);
        AssertLabel("Chroma NR +8 (+8)", after => after.Detail.ChromaNr = 8);
        AssertLabel("Vignette -12 (-12)", after =>
            after.Effects = new EffectsSettings { Vignette = -12 });
        AssertLabel("Grain +9 (+9)", after =>
            after.Effects = new EffectsSettings { Grain = 9 });
        AssertLabel("Vertical +5 (+5)", after =>
            after.Geometry = new GeometrySettings { Vertical = 5 });
        AssertLabel("Horizontal -6 (-6)", after =>
            after.Geometry = new GeometrySettings { Horizontal = -6 });
        AssertLabel("Aspect +7 (+7)", after =>
            after.Geometry = new GeometrySettings { Aspect = 7 });
        AssertLabel("Distortion -8 (-8)", after =>
            after.Geometry = new GeometrySettings { Distortion = -8 });
        AssertLabel("Optics: distortion off", after =>
            after.Lens.Distortion = false);
        AssertLabel("Optics: chromatic aberration off", after =>
            after.Lens.ChromaticAberration = false);
        AssertLabel("Optics: vignetting on", after =>
            after.Lens.Vignetting = true);

        foreach (var band in Enum.GetValues<ColorMixerBand>())
        {
            AssertLabel($"{band} hue +12 (+12)", after =>
            {
                after.Mixer = new ColorMixerSettings();
                after.Mixer.GetBand(band).Hue = 12;
            });
            AssertLabel($"{band} saturation -9 (-9)", after =>
            {
                after.Mixer = new ColorMixerSettings();
                after.Mixer.GetBand(band).Saturation = -9;
            });
            AssertLabel($"{band} luminance +6 (+6)", after =>
            {
                after.Mixer = new ColorMixerSettings();
                after.Mixer.GetBand(band).Luminance = 6;
            });
        }

        var effects = new EditSettings
        {
            Effects = new EffectsSettings { Vignette = -10 }
        };
        var midpoint = effects.Clone();
        midpoint.Effects!.Midpoint = 60;
        Assert.Equal("Midpoint +60 (+10)",
            EditHistoryLabel.Derive(effects, midpoint));

        var whiteBalance = new EditSettings
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = 6500,
                Tint = 0
            }
        };
        var kelvin = whiteBalance.Clone();
        kelvin.Wb.Kelvin = 7000;
        Assert.Equal("Kelvin +7000 (+500)",
            EditHistoryLabel.Derive(whiteBalance, kelvin));
        var tint = whiteBalance.Clone();
        tint.Wb.Tint = -5;
        Assert.Equal("Tint -5 (-5)",
            EditHistoryLabel.Derive(whiteBalance, tint));
    }

    [Fact]
    public void NestedFamiliesLabelMultiFieldChanges()
    {
        var before = new EditSettings();
        var after = before.Clone();
        after.Detail.LuminanceNr = 10;
        after.Detail.ChromaNr = 10;
        Assert.Equal("Detail", EditHistoryLabel.Derive(before, after));

        after = before.Clone();
        after.Mixer = new ColorMixerSettings();
        after.Mixer.Red.Hue = 10;
        after.Mixer.Red.Saturation = 10;
        Assert.Equal("Color mixer", EditHistoryLabel.Derive(before, after));

        after = before.Clone();
        after.Curve.AddPointAndReturnIndex(.5, .6);
        Assert.Equal("Curve", EditHistoryLabel.Derive(before, after));
    }

    [Theory]
    [InlineData("Paste settings")]
    [InlineData("Preset: Classic Chrome")]
    [InlineData("Reset")]
    [InlineData("Profile: Adobe Standard")]
    [InlineData("Auto white balance")]
    public void OperationLabelIsFinalizedAtCommit(string label)
    {
        Assert.Equal(label, EditHistoryLabel.Derive(
            new EditSettings(), new EditSettings { Exposure = 1 }, label));
    }

    [Fact]
    public void BaseLookReceivesHumanLabel()
    {
        Assert.Equal("Base look", EditHistoryLabel.Derive(
            new EditSettings(), new EditSettings { BaseLook = true }));
    }

    [Theory]
    [InlineData(0, 90, "Rotate right")]
    [InlineData(270, 0, "Rotate right")]
    [InlineData(0, 270, "Rotate left")]
    [InlineData(90, 0, "Rotate left")]
    [InlineData(0, 180, "Rotate 180°")]
    public void RotationUsesModularDirection(int before, int after, string expected)
    {
        Assert.Equal(expected, EditHistoryLabel.Derive(
            new EditSettings { Rotation = before },
            new EditSettings { Rotation = after }));
    }

    [Fact]
    public void HorizonIncludesDegreesValueAndDelta()
    {
        Assert.Equal("Horizon +1.50° (+0.50°)", EditHistoryLabel.Derive(
            new EditSettings { HorizonRotation = 1 },
            new EditSettings { HorizonRotation = 1.5 }));
    }

    [Fact]
    public void CropLabelsSetClearAndCropModeOperation()
    {
        var uncropped = new EditSettings();
        var cropped = new EditSettings
        {
            Crop = new CropRegion { Left = .1, Right = .9 }
        };
        var horizonOnly = new EditSettings { HorizonRotation = 1 };

        Assert.Equal("Crop", EditHistoryLabel.Derive(uncropped, cropped));
        Assert.Equal("Crop cleared", EditHistoryLabel.Derive(cropped, uncropped));
        Assert.Equal("Crop", EditHistoryLabel.CropOperation(uncropped, cropped));
        Assert.Null(EditHistoryLabel.CropOperation(uncropped, horizonOnly));
    }

    private static void AssertLabel(string expected, Action<EditSettings> change)
    {
        var before = new EditSettings();
        var after = before.Clone();
        change(after);
        Assert.Equal(expected, EditHistoryLabel.Derive(before, after));
    }
}

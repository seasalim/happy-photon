using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditSettingsTransferTests
{
    private static CurveData CreateCurve()
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(0.5, 0.7);
        return curve;
    }

    private static EditSettings CreateFullSettings() => new()
    {
        Version = EditSettings.CurrentVersion,
        Exposure = 1.5,
        Wb = new WhiteBalanceSettings
        {
            Mode = WbMode.Custom,
            Kelvin = 7200,
            Tint = 10
        },
        Brightness = 10,
        Contrast = -5,
        Saturation = 15,
        Vibrance = 25,
        Shadows = 30,
        Highlights = -40,
        BaseLook = true,
        HlReconstruction = HlReconstructionMode.Blend,
        Detail = new DetailSettings
        {
            CaptureSharpen = 35,
            LuminanceNr = 46,
            ChromaNr = 12
        },
        Effects = new EffectsSettings
        {
            Vignette = -35,
            Midpoint = 62,
            Grain = 24,
            GrainSize = GrainSize.Coarse
        },
        Mixer = CreateMixer(),
        Rotation = 90,
        HorizonRotation = 1.5,
        Geometry = new GeometrySettings
        {
            Vertical = -18,
            Horizontal = 22,
            Aspect = 31,
            Distortion = -44
        },
        Crop = new CropRegion { Left = 0.1, Top = 0.2, Right = 0.8, Bottom = 0.9 },
        AppliedPresetId = "user_abc",
        RawProfile = CreateProfileSelection(),
        Curve = CreateCurve(),
        CurveRed = CreateCurve(),
        CurveGreen = CreateCurve(),
        CurveBlue = CreateCurve()
    };

    [Fact]
    public void CopySubset_CopiesTransferableFieldsAndZeroesGeometry()
    {
        var source = CreateFullSettings();

        var copy = EditSettingsTransfer.CopySubset(source);

        Assert.Equal(1.5, copy.Exposure);
        Assert.Equal(WbMode.Custom, copy.Wb.Mode);
        Assert.Equal(7200, copy.Wb.Kelvin);
        Assert.Equal(10, copy.Wb.Tint);
        Assert.Equal(10, copy.Brightness);
        Assert.Equal(-5, copy.Contrast);
        Assert.Equal(15, copy.Saturation);
        Assert.Equal(25, copy.Vibrance);
        Assert.Equal(30, copy.Shadows);
        Assert.Equal(-40, copy.Highlights);
        Assert.True(copy.BaseLook);
        Assert.Equal(HlReconstructionMode.Blend, copy.HlReconstruction);
        Assert.Equal(35, copy.Detail.CaptureSharpen);
        Assert.Equal(46, copy.Detail.LuminanceNr);
        Assert.Equal(12, copy.Detail.ChromaNr);
        Assert.Equal(-35, copy.Effects!.Vignette);
        Assert.Equal(62, copy.Effects.Midpoint);
        Assert.Equal(24, copy.Effects.Grain);
        Assert.Equal(GrainSize.Coarse, copy.Effects.GrainSize);
        Assert.Equal(-28, copy.Mixer!.Orange.Hue);
        Assert.Equal(44, copy.Mixer.Blue.Saturation);
        Assert.Equal("user_abc", copy.AppliedPresetId);
        Assert.Equal(source.Curve.Points.Count, copy.Curve.Points.Count);
        Assert.Equal(0.7, copy.Curve.Points[1].Y);
        Assert.Equal(0.7, copy.CurveRed!.Points[1].Y);
        Assert.Equal(0.7, copy.CurveGreen!.Points[1].Y);
        Assert.Equal(0.7, copy.CurveBlue!.Points[1].Y);
        Assert.Equal(0, copy.Rotation);
        Assert.Equal(0.0, copy.HorizonRotation);
        Assert.Null(copy.Crop);
        Assert.Null(copy.Geometry);
        Assert.Null(copy.RawProfile);
        Assert.Equal(EditSettings.CurrentVersion, copy.Version);
    }

    [Fact]
    public void ApplySubset_PreservesTargetGeometry()
    {
        var copied = EditSettingsTransfer.CopySubset(CreateFullSettings());
        var target = new EditSettings
        {
            Version = EditSettings.CurrentVersion,
            Detail = new DetailSettings
            {
                CaptureSharpen = 80,
                LuminanceNr = 92,
                ChromaNr = 60
            },
            Rotation = 270,
            HorizonRotation = -3.0,
            Geometry = new GeometrySettings { Vertical = 73 },
            Crop = new CropRegion { Left = 0.25, Top = 0.25, Right = 0.75, Bottom = 0.75 },
            RawProfile = CreateProfileSelection()
        };

        EditSettingsTransfer.ApplySubset(copied, target);

        Assert.Equal(1.5, target.Exposure);
        Assert.Equal(WbMode.Custom, target.Wb.Mode);
        Assert.Equal(7200, target.Wb.Kelvin);
        Assert.Equal(10, target.Wb.Tint);
        Assert.Equal(10, target.Brightness);
        Assert.Equal(-5, target.Contrast);
        Assert.Equal(15, target.Saturation);
        Assert.Equal(25, target.Vibrance);
        Assert.Equal(30, target.Shadows);
        Assert.Equal(-40, target.Highlights);
        Assert.True(target.BaseLook);
        Assert.Equal(HlReconstructionMode.Blend, target.HlReconstruction);
        Assert.Equal(35, target.Detail.CaptureSharpen);
        Assert.Equal(46, target.Detail.LuminanceNr);
        Assert.Equal(12, target.Detail.ChromaNr);
        Assert.Equal(-35, target.Effects!.Vignette);
        Assert.NotSame(copied.Effects, target.Effects);
        Assert.Equal(-28, target.Mixer!.Orange.Hue);
        Assert.NotSame(copied.Mixer, target.Mixer);
        Assert.Equal("user_abc", target.AppliedPresetId);
        Assert.Equal(EditSettings.CurrentVersion, target.Version);
        Assert.Equal(270, target.Rotation);
        Assert.Equal(-3.0, target.HorizonRotation);
        Assert.Equal(73, target.Geometry?.Vertical);
        Assert.NotNull(target.Crop);
        Assert.Equal(0.25, target.Crop!.Left);
        Assert.NotNull(target.RawProfile);
    }

    [Fact]
    public void CurveIsDeepClonedInBothDirections()
    {
        var source = CreateFullSettings();
        var copy = EditSettingsTransfer.CopySubset(source);

        source.Curve.MovePoint(1, 0.5, 0.1);
        source.CurveRed!.MovePoint(1, 0.5, 0.1);
        Assert.Equal(0.7, copy.Curve.Points[1].Y);
        Assert.Equal(0.7, copy.CurveRed!.Points[1].Y);

        var targetA = new EditSettings();
        var targetB = new EditSettings();
        EditSettingsTransfer.ApplySubset(copy, targetA);
        EditSettingsTransfer.ApplySubset(copy, targetB);

        targetA.Curve.MovePoint(1, 0.5, 0.9);
        targetA.CurveBlue!.MovePoint(1, 0.5, 0.9);
        Assert.Equal(0.7, targetB.Curve.Points[1].Y);
        Assert.Equal(0.7, targetB.CurveBlue!.Points[1].Y);
        Assert.Equal(0.7, copy.Curve.Points[1].Y);
        Assert.Equal(0.7, copy.CurveBlue!.Points[1].Y);
    }

    [Fact]
    public void EffectsJoinCloneHasEditsAndHistoryEquality()
    {
        var source = new EditSettings
        {
            Effects = new EffectsSettings
            {
                Vignette = 25,
                Midpoint = 65,
                Grain = 18,
                GrainSize = GrainSize.Fine
            }
        };
        var clone = source.Clone();

        Assert.True(source.HasEdits);
        Assert.True(source.EqualsIgnoringRotation(clone));
        Assert.NotSame(source.Effects, clone.Effects);

        clone.Effects!.Grain = 19;
        Assert.False(source.EqualsIgnoringRotation(clone));
        Assert.True(new EditSettings().EqualsIgnoringRotation(
            new EditSettings
            {
                Effects = new EffectsSettings
                {
                    Midpoint = 99,
                    GrainSize = GrainSize.Coarse
                }
            }));
    }

    [Fact]
    public void MixerJoinsCloneHasEditsAndHistoryEquality()
    {
        var source = new EditSettings { Mixer = CreateMixer() };
        var clone = source.Clone();

        Assert.True(source.HasEdits);
        Assert.True(source.EqualsIgnoringRotation(clone));
        Assert.NotSame(source.Mixer, clone.Mixer);
        Assert.NotSame(source.Mixer!.Orange, clone.Mixer!.Orange);

        clone.Mixer.Orange.Hue++;
        Assert.False(source.EqualsIgnoringRotation(clone));
        Assert.True(new EditSettings().EqualsIgnoringRotation(
            new EditSettings { Mixer = new ColorMixerSettings() }));
    }

    [Fact]
    public void ApplySubset_HasEditsReflectsResult()
    {
        var target = new EditSettings();
        EditSettingsTransfer.ApplySubset(EditSettingsTransfer.CopySubset(CreateFullSettings()), target);
        Assert.True(target.HasEdits);

        var croppedOnly = new EditSettings
        {
            Crop = new CropRegion { Left = 0.1, Top = 0.1, Right = 0.9, Bottom = 0.9 }
        };
        EditSettingsTransfer.ApplySubset(EditSettingsTransfer.CopySubset(new EditSettings()), croppedOnly);
        Assert.True(croppedOnly.HasEdits);
    }

    [Fact]
    public void ChannelCurvesJoinCloneAndHasEdits()
    {
        var source = new EditSettings { CurveRed = CreateCurve() };

        var clone = source.Clone();

        Assert.True(source.HasEdits);
        Assert.True(clone.HasEdits);
        Assert.NotSame(source.CurveRed, clone.CurveRed);
        source.CurveRed!.MovePoint(1, 0.5, 0.2);
        Assert.Equal(0.7, clone.CurveRed!.Points[1].Y);
        Assert.False(new EditSettings { CurveRed = new CurveData() }.HasEdits);
    }

    [Fact]
    public void ApplySubset_AppliedCurveHasRebuiltLookupTable()
    {
        var target = new EditSettings();

        EditSettingsTransfer.ApplySubset(EditSettingsTransfer.CopySubset(CreateFullSettings()), target);

        Assert.True(target.Curve.LookupTable[128] > 140);
        Assert.True(target.CurveRed!.LookupTable[128] > 140);
    }

    [Fact]
    public void ApplySubset_IgnoresGeometryOnCopiedObject()
    {
        var copied = new EditSettings
        {
            Exposure = 0.5,
            Rotation = 180,
            HorizonRotation = 5,
            Geometry = new GeometrySettings { Vertical = 100 }
        };
        var target = new EditSettings
        {
            Rotation = 90,
            Geometry = new GeometrySettings { Vertical = -25 }
        };

        EditSettingsTransfer.ApplySubset(copied, target);

        Assert.Equal(0.5, target.Exposure);
        Assert.Equal(90, target.Rotation);
        Assert.Equal(0.0, target.HorizonRotation);
        Assert.Equal(-25, target.Geometry?.Vertical);
    }

    [Fact]
    public void Transfer_RejectsUnsupportedVersions()
    {
        var unsupported = new EditSettings { Version = 1 };

        Assert.Throws<NotSupportedException>(() =>
            EditSettingsTransfer.CopySubset(unsupported));
        Assert.Throws<NotSupportedException>(() =>
            EditSettingsTransfer.ApplySubset(unsupported, new EditSettings()));
        Assert.Throws<NotSupportedException>(() =>
            EditSettingsTransfer.ApplySubset(
                new EditSettings(),
                new EditSettings { Version = 1 }));
    }

    [Theory]
    [InlineData(null, 48)]
    [InlineData(0, 0)]
    [InlineData(25, 73)]
    public void CopyAndApply_PreserveNullableDetailSemantics(
        int? captureSharpen,
        int targetSharpen)
    {
        var source = new EditSettings
        {
            Detail = new DetailSettings
            {
                CaptureSharpen = captureSharpen,
                LuminanceNr = 87,
                ChromaNr = 61
            }
        };
        var target = new EditSettings
        {
            Detail = new DetailSettings
            {
                CaptureSharpen = targetSharpen,
                LuminanceNr = 12,
                ChromaNr = 12
            }
        };

        var copied = EditSettingsTransfer.CopySubset(source);
        EditSettingsTransfer.ApplySubset(copied, target);

        Assert.Equal(captureSharpen, copied.Detail.CaptureSharpen);
        Assert.Equal(captureSharpen, target.Detail.CaptureSharpen);
        Assert.Equal(87, target.Detail.LuminanceNr);
        Assert.Equal(61, target.Detail.ChromaNr);
        Assert.NotSame(source.Detail, copied.Detail);
        Assert.NotSame(copied.Detail, target.Detail);
    }

    private static RawProfileSelection CreateProfileSelection() => new()
    {
        Source = RawProfileSource.UserFile,
        Location = "synthetic.dcp",
        ContentHash = new string('a', 64)
    };

    private static ColorMixerSettings CreateMixer()
    {
        var mixer = new ColorMixerSettings();
        mixer.Orange.Hue = -28;
        mixer.Blue.Saturation = 44;
        mixer.Magenta.Luminance = 19;
        return mixer;
    }
}

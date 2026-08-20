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
            NoiseReduction = FbddMode.Light,
            ChromaNr = 12
        },
        Rotation = 90,
        HorizonRotation = 1.5,
        Crop = new CropRegion { Left = 0.1, Top = 0.2, Right = 0.8, Bottom = 0.9 },
        AppliedPresetId = "user_abc",
        Curve = CreateCurve()
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
        Assert.Equal(FbddMode.Light, copy.Detail.NoiseReduction);
        Assert.Equal(12, copy.Detail.ChromaNr);
        Assert.Equal("user_abc", copy.AppliedPresetId);
        Assert.Equal(source.Curve.Points.Count, copy.Curve.Points.Count);
        Assert.Equal(0.7, copy.Curve.Points[1].Y);
        Assert.Equal(0, copy.Rotation);
        Assert.Equal(0.0, copy.HorizonRotation);
        Assert.Null(copy.Crop);
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
                NoiseReduction = FbddMode.Full,
                ChromaNr = 60
            },
            Rotation = 270,
            HorizonRotation = -3.0,
            Crop = new CropRegion { Left = 0.25, Top = 0.25, Right = 0.75, Bottom = 0.75 }
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
        Assert.Equal(FbddMode.Light, target.Detail.NoiseReduction);
        Assert.Equal(12, target.Detail.ChromaNr);
        Assert.Equal("user_abc", target.AppliedPresetId);
        Assert.Equal(EditSettings.CurrentVersion, target.Version);
        Assert.Equal(270, target.Rotation);
        Assert.Equal(-3.0, target.HorizonRotation);
        Assert.NotNull(target.Crop);
        Assert.Equal(0.25, target.Crop!.Left);
    }

    [Fact]
    public void CurveIsDeepClonedInBothDirections()
    {
        var source = CreateFullSettings();
        var copy = EditSettingsTransfer.CopySubset(source);

        source.Curve.MovePoint(1, 0.5, 0.1);
        Assert.Equal(0.7, copy.Curve.Points[1].Y);

        var targetA = new EditSettings();
        var targetB = new EditSettings();
        EditSettingsTransfer.ApplySubset(copy, targetA);
        EditSettingsTransfer.ApplySubset(copy, targetB);

        targetA.Curve.MovePoint(1, 0.5, 0.9);
        Assert.Equal(0.7, targetB.Curve.Points[1].Y);
        Assert.Equal(0.7, copy.Curve.Points[1].Y);
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
    public void ApplySubset_AppliedCurveHasRebuiltLookupTable()
    {
        var target = new EditSettings();

        EditSettingsTransfer.ApplySubset(EditSettingsTransfer.CopySubset(CreateFullSettings()), target);

        Assert.True(target.Curve.LookupTable[128] > 140);
    }

    [Fact]
    public void ApplySubset_IgnoresGeometryOnCopiedObject()
    {
        var copied = new EditSettings { Exposure = 0.5, Rotation = 180, HorizonRotation = 5.0 };
        var target = new EditSettings { Rotation = 90 };

        EditSettingsTransfer.ApplySubset(copied, target);

        Assert.Equal(0.5, target.Exposure);
        Assert.Equal(90, target.Rotation);
        Assert.Equal(0.0, target.HorizonRotation);
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
}

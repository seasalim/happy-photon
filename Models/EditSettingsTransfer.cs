using HappyPhoton.Services;

namespace HappyPhoton.Models;

/// <summary>
/// Policy for which Develop settings transfer between images.
/// Geometry (rotation, horizon rotation, crop, manual geometry) never transfers.
/// </summary>
public static class EditSettingsTransfer
{
    public static EditSettings CopySubset(EditSettings source)
    {
        var target = new EditSettings();
        ApplySubset(source, target);
        return target;
    }

    public static void ApplySubset(EditSettings copied, EditSettings target)
    {
        EditSettingsJson.EnsureCurrent(copied);
        EditSettingsJson.EnsureCurrent(target);
        target.Exposure = copied.Exposure;
        target.Wb = copied.Wb.Clone();
        target.Brightness = copied.Brightness;
        target.Contrast = copied.Contrast;
        target.Saturation = copied.Saturation;
        target.Vibrance = copied.Vibrance;
        target.Shadows = copied.Shadows;
        target.Highlights = copied.Highlights;
        target.BaseLook = copied.BaseLook;
        target.HlReconstruction = copied.HlReconstruction;
        target.Detail = copied.Detail.Clone();
        target.Effects = copied.Effects?.Clone();
        target.Mixer = copied.Mixer?.Clone();
        target.Lens.Distortion = copied.Lens.Distortion;
        target.Lens.ChromaticAberration = copied.Lens.ChromaticAberration;
        target.Lens.Vignetting = copied.Lens.Vignetting;
        target.Curve = copied.Curve.Clone();
        target.CurveRed = copied.CurveRed?.Clone();
        target.CurveGreen = copied.CurveGreen?.Clone();
        target.CurveBlue = copied.CurveBlue?.Clone();
        target.AppliedPresetId = copied.AppliedPresetId;
    }
}

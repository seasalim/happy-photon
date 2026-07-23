namespace HappyPhoton.Models;

/// <summary>
/// Policy for which edit settings transfer between images via copy/paste.
/// Geometry (rotation, horizon rotation, crop) never transfers in either direction.
/// </summary>
public static class EditSettingsTransfer
{
    public static EditSettings CopySubset(EditSettings source) => new()
    {
        Exposure = source.Exposure,
        Temperature = source.Temperature,
        Brightness = source.Brightness,
        Contrast = source.Contrast,
        Saturation = source.Saturation,
        Vibrance = source.Vibrance,
        Shadows = source.Shadows,
        Highlights = source.Highlights,
        Curve = source.Curve.Clone(),
        AppliedPresetId = source.AppliedPresetId
    };

    public static void ApplySubset(EditSettings copied, EditSettings target)
    {
        target.Exposure = copied.Exposure;
        target.Temperature = copied.Temperature;
        target.Brightness = copied.Brightness;
        target.Contrast = copied.Contrast;
        target.Saturation = copied.Saturation;
        target.Vibrance = copied.Vibrance;
        target.Shadows = copied.Shadows;
        target.Highlights = copied.Highlights;
        target.Curve = copied.Curve.Clone();
        target.AppliedPresetId = copied.AppliedPresetId;
    }
}

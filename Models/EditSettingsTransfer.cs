namespace HappyPhoton.Models;

/// <summary>
/// Policy for which Develop settings transfer between images.
/// Geometry (rotation, horizon rotation, crop) never transfers in either direction.
/// </summary>
public static class EditSettingsTransfer
{
    public static EditSettings CopySubset(EditSettings source)
    {
        EnsureCurrent(source);
        return new EditSettings
        {
            Exposure = source.Exposure,
            Wb = source.Wb.Clone(),
            Brightness = source.Brightness,
            Contrast = source.Contrast,
            Saturation = source.Saturation,
            Vibrance = source.Vibrance,
            Shadows = source.Shadows,
            Highlights = source.Highlights,
            BaseLook = source.BaseLook,
            HlReconstruction = source.HlReconstruction,
            Detail = source.Detail.Clone(),
            Curve = source.Curve.Clone(),
            AppliedPresetId = source.AppliedPresetId
        };
    }

    public static void ApplySubset(EditSettings copied, EditSettings target)
    {
        EnsureCurrent(copied);
        EnsureCurrent(target);
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
        target.Curve = copied.Curve.Clone();
        target.AppliedPresetId = copied.AppliedPresetId;
    }

    private static void EnsureCurrent(EditSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Version != EditSettings.CurrentVersion)
        {
            throw new NotSupportedException(
                $"Edit settings version {settings.Version} is not supported.");
        }
    }
}

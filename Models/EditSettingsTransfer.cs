using HappyPhoton.Services;

namespace HappyPhoton.Models;

public enum EditSettingsGroupKind
{
    Look,
    PhotoSpecific
}

public sealed class EditSettingsGroup
{
    internal EditSettingsGroup(string name, EditSettingsGroupKind kind, bool isDefault,
        string[] fields, Action<EditSettings, EditSettings> copy)
    {
        Name = name;
        Kind = kind;
        IsDefault = isDefault;
        Fields = Array.AsReadOnly(fields);
        Copy = copy;
    }

    public string Name { get; }

    public EditSettingsGroupKind Kind { get; }

    public bool IsDefault { get; }

    public IReadOnlyList<string> Fields { get; }

    internal Action<EditSettings, EditSettings> Copy { get; }
}

/// <summary>Whole-group transfer policy. History restores whole documents instead.</summary>
public static class EditSettingsTransfer
{
    public static IReadOnlyList<EditSettingsGroup> Groups { get; } = Array.AsReadOnly<EditSettingsGroup>(
    [
        new("White Balance", EditSettingsGroupKind.Look, true, ["wb"],
            (source, target) => target.Wb = source.Wb.Clone()),
        new("Adjustments", EditSettingsGroupKind.Look, true,
            ["exposure", "brightness", "contrast", "saturation", "vibrance", "shadows",
                "highlights", "whites", "blacks", "baseLook", "hlReconstruction"],
            (source, target) =>
            {
                target.Exposure = source.Exposure;
                target.Brightness = source.Brightness;
                target.Contrast = source.Contrast;
                target.Saturation = source.Saturation;
                target.Vibrance = source.Vibrance;
                target.Shadows = source.Shadows;
                target.Highlights = source.Highlights;

                target.Whites = source.Whites;
                target.Blacks = source.Blacks;
                target.BaseLook = source.BaseLook;
                target.HlReconstruction = source.HlReconstruction;
            }),
        // OPS's Presence panel; Dehaze joins this group when its field lands.
        new("Presence", EditSettingsGroupKind.Look, true, ["texture", "clarity"],
            (source, target) =>
            {
                target.Texture = source.Texture;
                target.Clarity = source.Clarity;
            }),
        new("Tone Curve", EditSettingsGroupKind.Look, true,
            ["curve", "curveRed", "curveGreen", "curveBlue"],
            (source, target) =>
            {
                target.Curve = source.Curve.Clone();
                target.CurveRed = source.CurveRed?.Clone();
                target.CurveGreen = source.CurveGreen?.Clone();
                target.CurveBlue = source.CurveBlue?.Clone();
            }),
        new("Color Mixer", EditSettingsGroupKind.Look, true, ["mixer"],
            (source, target) => target.Mixer = source.Mixer?.Clone()),
        new("Detail", EditSettingsGroupKind.Look, true, ["detail"],
            (source, target) => target.Detail = source.Detail.Clone()),
        new("Effects", EditSettingsGroupKind.Look, true, ["effects"],
            (source, target) => target.Effects = source.Effects?.Clone()),
        new("Optics", EditSettingsGroupKind.Look, true,
            ["lens.distortion", "lens.chromaticAberration", "lens.vignetting"],
            (source, target) =>
            {
                target.Lens.Distortion = source.Lens.Distortion;
                target.Lens.ChromaticAberration = source.Lens.ChromaticAberration;
                target.Lens.Vignetting = source.Lens.Vignetting;
            }),
        new("Crop & Straighten", EditSettingsGroupKind.PhotoSpecific, false,
            ["crop", "horizon_rotation"],
            (source, target) =>
            {
                target.Crop = source.Crop?.Clone();
                target.HorizonRotation = source.HorizonRotation;
            }),
        new("Geometry", EditSettingsGroupKind.PhotoSpecific, false, ["geometry"],
            (source, target) => target.Geometry = source.Geometry?.Clone()),
        new("Camera Profile", EditSettingsGroupKind.PhotoSpecific, false, ["rawProfile"],
            (source, target) => target.RawProfile = source.RawProfile?.Clone()),
        new("Lens Profile", EditSettingsGroupKind.PhotoSpecific, false, ["lens.profileOverride"],
            (source, target) => target.Lens.ProfileOverride = source.Lens.ProfileOverride),
        new("Locals", EditSettingsGroupKind.PhotoSpecific, false, ["locals"],
            (source, target) => target.Locals = source.Locals?.Select(local => local with { }).ToList()),
        new("Spot Removal", EditSettingsGroupKind.PhotoSpecific, false, ["repairs"],
            (source, target) => target.Repairs = source.Repairs?.Select(repair => repair with { }).ToList())
    ]);

    public static IReadOnlyList<string> NeverTransfers { get; } =
        Array.AsReadOnly(new[] { "version", "rotation", "applied_preset_id" });

    public static IReadOnlyList<EditSettingsGroup> DefaultGroups { get; } =
        Array.AsReadOnly(Groups.Where(group => group.IsDefault).ToArray());

    public static IReadOnlyList<EditSettingsGroup> LookGroups { get; } =
        Array.AsReadOnly(Groups.Where(group => group.Kind == EditSettingsGroupKind.Look).ToArray());

    public static EditSettings CopyGroups(EditSettings source,
        IReadOnlyCollection<EditSettingsGroup>? groups = null)
    {
        var target = new EditSettings();
        ApplyGroups(source, target, groups);

        return target;
    }

    public static void ApplyGroups(EditSettings source, EditSettings target,
        IReadOnlyCollection<EditSettingsGroup>? groups = null)
    {
        EditSettingsJson.EnsureCurrent(source);
        EditSettingsJson.EnsureCurrent(target);
        groups ??= DefaultGroups;

        foreach (var group in groups)
        {
            group.Copy(source, target);
        }

        target.AppliedPresetId = LookGroups.All(groups.Contains) ? source.AppliedPresetId : null;
    }
}

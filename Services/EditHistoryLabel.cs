using System.Text.Json;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public static class EditHistoryLabel
{
    public static string Derive(EditSettings before, EditSettings after,
        string? operation = null)
    {
        if (!string.IsNullOrWhiteSpace(operation)) return operation;
        var changes = new List<string>();
        AddScalar(changes, "Exposure", before.Exposure, after.Exposure, "0.00");
        AddScalar(changes, "Brightness", before.Brightness, after.Brightness);
        AddScalar(changes, "Contrast", before.Contrast, after.Contrast);
        AddScalar(changes, "Highlights", before.Highlights, after.Highlights);
        AddScalar(changes, "Shadows", before.Shadows, after.Shadows);
        AddScalar(changes, "Saturation", before.Saturation, after.Saturation);
        AddScalar(changes, "Vibrance", before.Vibrance, after.Vibrance);
        Add(changes, "Base look", before.BaseLook, after.BaseLook);

        var beforeWb = before.Wb ?? new WhiteBalanceSettings();
        var afterWb = after.Wb ?? new WhiteBalanceSettings();
        var wbScalars = new[]
        {
            Scalar("Kelvin", beforeWb.Kelvin, afterWb.Kelvin),
            Scalar("Tint", beforeWb.Tint, afterWb.Tint, nullEqualsZero: true)
        };
        var wbDescriptorChanged = beforeWb.Mode != afterWb.Mode ||
            beforeWb.Preset != afterWb.Preset;
        AddFamily(changes, "White balance", wbScalars,
            !Enumerable.SequenceEqual(beforeWb.Gains ?? [], afterWb.Gains ?? []) ||
            wbScalars.All(change => change == null) && wbDescriptorChanged);

        Add(changes, "Curve", new[] { before.Curve, before.CurveRed,
            before.CurveGreen, before.CurveBlue }, new[] { after.Curve,
            after.CurveRed, after.CurveGreen, after.CurveBlue });

        AddFamily(changes, "Detail",
        [
            Scalar("Sharpen", before.Detail.CaptureSharpen,
                after.Detail.CaptureSharpen),
            Scalar("Luma NR", before.Detail.LuminanceNr,
                after.Detail.LuminanceNr),
            Scalar("Chroma NR", before.Detail.ChromaNr, after.Detail.ChromaNr)
        ]);

        var beforeEffects = before.Effects ?? new EffectsSettings();
        var afterEffects = after.Effects ?? new EffectsSettings();
        AddFamily(changes, "Effects",
        [
            Scalar("Vignette", beforeEffects.Vignette, afterEffects.Vignette),
            Scalar("Midpoint", beforeEffects.Midpoint, afterEffects.Midpoint),
            Scalar("Grain", beforeEffects.Grain, afterEffects.Grain)
        ], beforeEffects.GrainSize != afterEffects.GrainSize);

        var beforeMixer = before.Mixer ?? new ColorMixerSettings();
        var afterMixer = after.Mixer ?? new ColorMixerSettings();
        var mixerChanges = new List<string?>();
        foreach (var band in Enum.GetValues<ColorMixerBand>())
        {
            var oldBand = beforeMixer.GetBand(band);
            var newBand = afterMixer.GetBand(band);
            mixerChanges.Add(Scalar($"{band} hue", oldBand.Hue, newBand.Hue));
            mixerChanges.Add(Scalar(
                $"{band} saturation", oldBand.Saturation, newBand.Saturation));
            mixerChanges.Add(Scalar(
                $"{band} luminance", oldBand.Luminance, newBand.Luminance));
        }
        AddFamily(changes, "Color mixer", mixerChanges);

        var beforeGeometry = before.Geometry ?? new GeometrySettings();
        var afterGeometry = after.Geometry ?? new GeometrySettings();
        AddFamily(changes, "Geometry",
        [
            Scalar("Vertical", beforeGeometry.Vertical, afterGeometry.Vertical),
            Scalar("Horizontal", beforeGeometry.Horizontal, afterGeometry.Horizontal),
            Scalar("Aspect", beforeGeometry.Aspect, afterGeometry.Aspect),
            Scalar("Distortion", beforeGeometry.Distortion,
                afterGeometry.Distortion)
        ]);

        AddFamily(changes, "Optics",
        [
            Toggle("Optics: distortion", before.Lens.Distortion,
                after.Lens.Distortion),
            Toggle("Optics: chromatic aberration", before.Lens.ChromaticAberration,
                after.Lens.ChromaticAberration),
            Toggle("Optics: vignetting", before.Lens.Vignetting,
                after.Lens.Vignetting)
        ], before.Lens.Baseline != after.Lens.Baseline);
        Add(changes, "Highlight handling", before.HlReconstruction,
            after.HlReconstruction);
        Add(changes, "Profile", before.RawProfile, after.RawProfile);
        return changes.Count switch
        {
            0 => "Edit",
            1 => changes[0],
            _ => string.Join(", ", changes.Take(3))
        };
    }

    private static void AddScalar(
        ICollection<string> changes,
        string name,
        double before,
        double after,
        string format = "0")
    {
        var change = Scalar(name, before, after, format);
        if (change != null) changes.Add(change);
    }

    private static string? Scalar(
        string name,
        double? before,
        double? after,
        string format = "0",
        bool nullEqualsZero = false)
    {
        if (before == after || nullEqualsZero &&
            before.GetValueOrDefault() == after.GetValueOrDefault())
        {
            return null;
        }
        var oldValue = before.GetValueOrDefault();
        var newValue = after.GetValueOrDefault();
        var signedFormat = $"+{format};-{format};0";
        return $"{name} {newValue.ToString(signedFormat)} " +
               $"({(newValue - oldValue).ToString(signedFormat)})";
    }

    private static string? Toggle(string name, bool before, bool after) =>
        before == after ? null : $"{name} {(after ? "on" : "off")}";

    private static void AddFamily(
        ICollection<string> changes,
        string family,
        IEnumerable<string?> candidates,
        bool otherChanged = false)
    {
        var fields = candidates.Where(candidate => candidate != null).ToArray();
        if (fields.Length == 1 && !otherChanged)
            changes.Add(fields[0]!);
        else if (fields.Length > 0 || otherChanged)
            changes.Add(family);
    }

    private static void Add<T>(
        ICollection<string> changes,
        string label,
        T before,
        T after)
    {
        if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after))
            changes.Add(label);
    }
}

using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed record PhotoCameraFacts(CameraIdentity? Identity, bool IsMonochrome);

internal sealed record PhotoProfileSnapshot(string FilePath, bool IsRaw, PhotoCameraFacts? Camera);

internal static class ProfileSettingsTransfer
{
    internal static EditSettingsGroup[] CompatibleGroups(PhotoProfileSnapshot source, EditSettings settings,
        ImageFile target, IReadOnlyCollection<EditSettingsGroup> groups, PhotoFrameFactsReader reader,
        Dictionary<string, string> skipped, bool cachedOnly)
    {
        var sameFile = string.Equals(source.FilePath, target.FilePath, StringComparison.OrdinalIgnoreCase);
        PhotoCameraFacts? facts = null;
        var factsRead = false;
        var compatible = new List<EditSettingsGroup>();

        foreach (var group in groups)
        {
            var camera = group.Name == "Camera Profile";
            var lens = group.Name == "Lens Profile";
            string? reason = null;

            if ((camera || lens) && !sameFile)
            {
                var needsFacts = camera ? settings.RawProfile != null : settings.Lens.ProfileOverride != null;

                if (!target.IsRaw)
                {
                    if (needsFacts) skipped[group.Name] = "not RAW";

                    continue;
                }

                if (needsFacts)
                {
                    if (camera && settings.RawProfile!.Source == RawProfileSource.Embedded)
                    {
                        reason = "embedded in another photo";
                    }
                    else if (camera && (!source.IsRaw || string.IsNullOrWhiteSpace(source.Camera?.Identity?.Normalized)))
                    {
                        reason = "facts unavailable";
                    }
                    else
                    {
                        if (!factsRead)
                        {
                            facts = reader.ReadCamera(target, cachedOnly);
                            factsRead = true;
                        }

                        reason = camera ? CameraReason(source, facts) : LensReason(settings.Lens.ProfileOverride!, facts);
                    }
                }
            }

            if (reason == null)
            {
                compatible.Add(group);
            }
            else
            {
                skipped[group.Name] = reason;
            }
        }

        return compatible.ToArray();
    }

    private static string? CameraReason(PhotoProfileSnapshot source, PhotoCameraFacts? target)
    {
        if (target?.IsMonochrome == true) return "monochrome";
        if (!source.IsRaw || string.IsNullOrWhiteSpace(source.Camera?.Identity?.Normalized) ||
            string.IsNullOrWhiteSpace(target?.Identity?.Normalized))
            return "facts unavailable";

        return source.Camera.Identity.Normalized == target.Identity.Normalized ? null : "different camera";
    }

    private static string? LensReason(string lens, PhotoCameraFacts? target)
    {
        if (target?.IsMonochrome == true) return "monochrome";
        if (target?.Identity == null) return "facts unavailable";

        var choices = new LensfunPrescriptionReader().ListCompatibleLenses(target.Identity);
        if (choices.Camera == null) return "facts unavailable";

        return choices.Lenses.Contains(lens) ? null : "outside the lens mount";
    }
}

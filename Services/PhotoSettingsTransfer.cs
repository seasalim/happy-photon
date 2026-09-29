using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal static class PhotoSettingsTransfer
{
    internal static EditSettings Apply(ImageFile sourceFile, EditSettings source, ImageFile target,
        EditSettings previous, IReadOnlyCollection<EditSettingsGroup> groups, PhotoFrameFactsReader reader,
        out bool reframed, out bool factsUnavailable, bool cachedOnly = false)
    {
        var settings = previous.Clone();
        EditSettingsTransfer.ApplyGroups(source, settings, groups);
        reframed = false;
        factsUnavailable = false;

        if (groups.Any(group => group.Name == "Crop & Straighten") && source.Crop is { IsFullImage: false } crop)
        {
            var from = reader.Read(sourceFile, source, cachedOnly);
            var to = from.HasValue ? reader.Read(target, settings, cachedOnly) : null;

            if (from.HasValue && to.HasValue)
            {
                settings.Crop = CropTransfer.Apply(crop, from.Value, to.Value);
                reframed = !CropTransfer.Matches(from.Value, to.Value);
            }
            else
            {
                settings.Crop = previous.Crop?.Clone();
                settings.HorizonRotation = previous.HorizonRotation;
                factsUnavailable = true;
                if (groups.Count == 1) return previous.Clone();
            }
        }

        // Use the same validation and canonicalization as catalog saves before publishing models.
        EditSettingsJson.ValidateForSave(settings);

        return settings;
    }
}

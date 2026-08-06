using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

internal static class ThumbnailResidencyPolicy
{
    public static IReadOnlyList<ImageFile> SelectEvictions(
        IReadOnlyCollection<ImageFile> residents,
        IReadOnlySet<ImageFile> pinned,
        IReadOnlyDictionary<ImageFile, long> lastAccess,
        int targetCount)
    {
        var removeCount = Math.Max(0, residents.Count - Math.Max(0, targetCount));
        if (removeCount == 0) return Array.Empty<ImageFile>();
        return residents
            .Where(image => !pinned.Contains(image))
            .OrderBy(image => lastAccess.GetValueOrDefault(image))
            .Take(removeCount)
            .ToList();
    }
}

using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

internal static class ThumbnailResidencyPolicy
{
    public static IReadOnlyList<ImageFile> SelectEvictions(
        IReadOnlyCollection<ImageFile> residents,
        IReadOnlySet<ImageFile> pinned,
        IReadOnlyDictionary<ImageFile, long> lastAccess,
        long targetBytes)
    {
        var residentBytes = residents.Sum(image => image.ThumbnailBytes);
        if (residentBytes <= Math.Max(0, targetBytes))
        {
            return Array.Empty<ImageFile>();
        }

        var evictions = new List<ImageFile>();
        foreach (var image in residents
            .Where(image => !pinned.Contains(image))
            .OrderBy(image => lastAccess.GetValueOrDefault(image)))
        {
            evictions.Add(image);
            residentBytes -= image.ThumbnailBytes;
            if (residentBytes <= targetBytes) break;
        }

        return evictions;
    }
}

using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>Per-image cache files keyed by catalog id, shared by the thumbnail and preview caches.</summary>
internal static class CatalogCacheFile
{
    public static string PathFor(ImageFile imageFile, Func<long, string> pathForId)
    {
        if (imageFile.CatalogId == 0)
            throw new InvalidOperationException("Image must be in catalog before caching.");

        return pathForId(imageFile.CatalogId);
    }

    /// <summary>True when the cache file exists and is newer than the source.</summary>
    public static bool IsNewerThanSource(ImageFile imageFile, Func<long, string> pathForId)
    {
        if (imageFile.CatalogId == 0) return false;

        var path = pathForId(imageFile.CatalogId);
        if (!File.Exists(path)) return false;

        try
        {
            return File.GetLastWriteTimeUtc(path) > File.GetLastWriteTimeUtc(imageFile.FilePath);
        }
        catch
        {
            return false;
        }
    }
}

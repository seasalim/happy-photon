namespace HappyPhoton.Services;

/// <summary>
/// Guards exports against overwriting original image files. Paths are
/// normalized with Path.GetFullPath and compared OrdinalIgnoreCase on every
/// platform — on case-sensitive filesystems this can block a technically
/// distinct path that differs only by case, which is the safe direction.
/// </summary>
public static class ExportSafety
{
    public static HashSet<string> BuildOriginalPathSet(IEnumerable<string> originalPaths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in originalPaths)
        {
            try
            {
                set.Add(Path.GetFullPath(path));
            }
            catch (Exception)
            {
                // Library paths come from directory enumeration; an unparseable
                // entry is near-impossible. Skip it — the set builder never throws.
            }
        }
        return set;
    }

    public static bool IsOriginalPath(string targetPath, HashSet<string> originalPathSet)
    {
        try
        {
            return originalPathSet.Contains(Path.GetFullPath(targetPath));
        }
        catch (Exception)
        {
            return true;   // Unparseable target: fail safe — treat as collision.
        }
    }
}

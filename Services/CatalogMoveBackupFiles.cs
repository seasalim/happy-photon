namespace HappyPhoton.Services;

internal static class CatalogMoveBackupFiles
{
    internal static string PathFor(string root, string name = "")
    {
        using var catalog = new CatalogService(root);
        var folder = new CatalogBackupService(catalog).Folder;

        return CatalogRestoreExecutor.OwnedPath(root, Path.GetRelativePath(root, Path.Combine(folder, name)));
    }

    internal static void AssertLocal(string path, Func<string, FileAttributes> attributes)
    {
        if (((int)attributes(path) & (0x1000 | 0x40000 | 0x400000)) != 0)
            throw new IOException($"The catalog move cannot carry cloud-only backup '{path}'. Make it available locally first.");
    }

    internal static string[] LocalFiles(string root, Func<string, FileAttributes> attributes)
    {
        var folder = PathFor(root);
        if (!Directory.Exists(folder)) return [];

        var result = new List<string>();
        Collect(folder);

        return result.ToArray();

        void Collect(string directory)
        {
            AssertLocal(directory, attributes);

            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var name = Path.GetRelativePath(folder, path);
                AssertLocal(path, attributes);
                PathFor(root, name);

                if (Directory.Exists(path))
                {
                    Collect(path);
                }
                else
                {
                    result.Add(name);
                }
            }
        }
    }

    internal static void Verify(string root, Dictionary<string, string>? files,
        Func<string, FileAttributes> attributes)
    {
        foreach (var (name, hash) in files ?? [])
        {
            var path = PathFor(root, name);
            AssertLocal(path, attributes);
            if (CatalogBackupService.Hash(path) != hash)
                throw new IOException($"The copied backup '{name}' did not verify.");
        }
    }

    internal static void Cleanup(string root, Dictionary<string, string>? files)
    {
        AppDataRootOwnership.AssertAppOwned(root);

        foreach (var name in (files ?? []).Keys)
        {
            var path = PathFor(root, name);
            if (File.Exists(path)) File.Delete(path);
            var parent = Path.GetDirectoryName(path)!;

            // OwnedPath resolves existing ancestors even when a prior cleanup removed descendants.
            while (!CatalogMoveFileOperations.SamePath(parent, root))
            {
                if (Directory.Exists(parent))
                {
                    if (Directory.EnumerateFileSystemEntries(parent).Any()) break;

                    Directory.Delete(parent);
                }

                parent = Path.GetDirectoryName(parent)!;
            }
        }
    }
}

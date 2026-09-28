using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

internal static class CatalogRestorePreservation
{
    internal static IEnumerable<string> Files(string root)
    {
        foreach (var name in new[] { "catalog.db", "catalog.db-journal", ".catalog-identity" })
            if (File.Exists(Path.Combine(root, name))) yield return name;
        var presets = CatalogRestoreExecutor.OwnedPath(root, "presets");
        if (Directory.Exists(presets))
            foreach (var file in Directory.EnumerateFiles(presets, "*", SearchOption.AllDirectories))
                yield return Path.GetRelativePath(root, file).Replace('\\', '/');
    }

    internal static void CleanupPartials(string root, string stem, Action<string, Action> change)
    {
        foreach (var suffix in new[] { ".partial.zip", ".partial.json" })
        {
            var path = CatalogRestoreExecutor.OwnedPath(root, Path.GetRelativePath(root, stem + suffix));
            if (File.Exists(path)) change("preserve-cleanup:" + suffix, () => File.Delete(path));
        }
    }

    internal static void CleanupStaging(string root, string staging, Action<string, Action> change)
    {
        if (!Directory.Exists(staging)) return;
        CatalogRestoreExecutor.OwnedPath(root, Path.GetRelativePath(root, staging));
        var marker = Path.Combine(staging, AppDataRootOwnership.MarkerFileName);
        var owned = File.Exists(marker);
        if (owned) AppDataRootOwnership.AssertAppOwned(staging);
        var entries = Collect(staging).OrderByDescending(path => path.Length).ToArray();
        foreach (var path in entries.Where(path => path != marker))
            change("staging-delete:" + Path.GetRelativePath(staging, path).Replace('\\', '/'), () =>
            {
                if (Directory.Exists(path)) Directory.Delete(path);
                else File.Delete(path);
            });
        // Keep ownership until every payload file and child directory has gone.
        if (owned) change("staging-marker-delete", () => File.Delete(marker));
        change("staging-delete", () => Directory.Delete(staging));

        IEnumerable<string> Collect(string directory)
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Restore cleanup refuses linked staging entries.");
                var relative = Path.GetRelativePath(staging, path).Replace('\\', '/');
                var name = relative.StartsWith("preservation/", StringComparison.Ordinal) ? relative[13..] : relative;
                if (!owned && name != AppDataRootOwnership.MarkerFileName && name != "preservation" &&
                    name != "presets" && !name.StartsWith("presets/", StringComparison.Ordinal) &&
                    name is not "catalog.db" and not "catalog.db-journal" and not ".catalog-identity")
                    throw new AppDataOwnershipException("Restore staging contains an unrecognized entry.");
                if (Directory.Exists(path))
                    foreach (var child in Collect(path)) yield return child;
                yield return path;
            }
        }
    }

    internal static void Create(string root, string staging, string stem, Action<string, Action> change,
        string kind = "before-restore", Action<string>? verify = null)
    {
        var folder = Path.GetDirectoryName(stem)!;
        CatalogRestoreExecutor.OwnedPath(root, Path.GetRelativePath(root, folder));
        Directory.CreateDirectory(folder);
        var copyRoot = Path.Combine(staging, "preservation");
        AppDataRootOwnership.ClaimFresh(copyRoot);
        var entries = new Dictionary<string, BackupEntry>();
        foreach (var name in Files(root))
        {
            var source = CatalogRestoreExecutor.OwnedPath(root, name);
            var copy = CatalogRestoreExecutor.OwnedPath(copyRoot, name);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            change("preserve-copy:" + name, () => File.Copy(source, copy));
            entries[name] = new(new FileInfo(copy).Length, CatalogBackupService.Hash(copy));
        }
        CleanupPartials(root, stem, change);
        change("preserve-zip", () =>
        {
            using var zip = ZipFile.Open(stem + ".partial.zip", ZipArchiveMode.Create);
            foreach (var name in entries.Keys) zip.CreateEntryFromFile(Path.Combine(copyRoot, name), name);
            var damaged = false;
            (long Rows, long Schema) facts = default;
            try
            {
                // SQLite may recover a hot journal here, only on the already packaged copy.
                using var connection = CatalogService.OpenBackupCopy(Path.Combine(copyRoot, "catalog.db"), SqliteOpenMode.ReadWrite);
                using var check = connection.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                damaged = !Equals(check.ExecuteScalar(), "ok");
                facts = CatalogService.ReadBackupFacts(connection);
            }
            catch (Exception ex) when (ex is SqliteException or IOException) { damaged = true; }
            var manifest = new BackupManifest(1, kind, DateTimeOffset.UtcNow,
                AppBuildInfo.Version.ToString(), CatalogBackupService.ReadIdentity(root) ?? Guid.Empty,
                facts.Schema, facts.Rows, entries, damaged);
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            writer.Write(JsonSerializer.Serialize(manifest));
        });
        verify?.Invoke(stem + ".partial.zip");
        var verified = CatalogBackupService.VerifyArchive(stem + ".partial.zip");
        change("preserve-sidecar", () => File.WriteAllText(stem + ".partial.json", JsonSerializer.Serialize(verified)));
        change("preserve-publish", () => File.Move(stem + ".partial.zip", stem + ".zip"));
        change("preserve-publish-sidecar", () => File.Move(stem + ".partial.json", stem + ".manifest.json"));
    }
}

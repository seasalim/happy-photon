using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public sealed record CatalogBackupRow(string Path, string Description, string State, bool CanRestore, DateTimeOffset? Utc = null);

internal sealed record CheckedCatalogBackup(string Directory, BackupManifest Manifest,
    string ArchiveHash, string ManifestHash) : IDisposable
{
    public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
}

public sealed partial class CatalogBackupService
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Damaged =
        new(StringComparer.OrdinalIgnoreCase);
    internal static void MarkDamaged(string path) => Damaged.TryAdd(System.IO.Path.GetFullPath(path), 0);
    internal Func<string, FileAttributes> Attributes { get; set; } = File.GetAttributes;
    private bool CloudOnly(string path) => ((int)Attributes(path) & (0x1000 | 0x40000 | 0x400000)) != 0;

    public IReadOnlyList<CatalogBackupRow> ListForRestore()
    {
        var folder = Folder;

        if (!Directory.Exists(folder)) return [];

        return Directory.GetFiles(folder, "*.zip").Where(p => !p.Contains(".partial."))
            .Select(path =>
            {
                BackupManifest? manifest = null;
                var sidecar = Path.ChangeExtension(path, ".manifest.json");
                var cloud = CloudOnly(path);

                try
                {
                    if (File.Exists(sidecar) && !CloudOnly(sidecar))
                    {
                        manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(sidecar));
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }

                if (manifest is not { FormatVersion: 1, Entries: not null } ||
                    !manifest.Entries.ContainsKey("catalog.db") || !manifest.Entries.ContainsKey(".catalog-identity"))
                {
                    manifest = null;
                }

                var state = Damaged.ContainsKey(path) || manifest?.Damaged == true ? "damaged" :
                    manifest?.SchemaVersion > CatalogMigrations.CurrentVersion ? $"needs a newer app (schema {manifest.SchemaVersion})" :
                    cloud ? "cloud-only · downloads when restored" :
                    manifest is { FormatVersion: 1, Entries: not null } ? "verified when created" : "not checked";
                var description = manifest == null ? System.IO.Path.GetFileName(path) :
                    $"{manifest.Utc.LocalDateTime.ToString("MMM d, yyyy h:mm tt", System.Globalization.CultureInfo.InvariantCulture)} · {manifest.Kind} · {new FileInfo(path).Length / 1000000d:F1} MB · {manifest.ImageRows:N0} images · {manifest.AppVersion}";

                return new CatalogBackupRow(path, description, state,
                    state is not "damaged" && !state.StartsWith("needs a newer app"), manifest?.Utc);
            }).OrderByDescending(row => row.Utc).ToArray();
    }

    internal CheckedCatalogBackup CheckForRestore(string path)
    {
        var temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hp-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            Step?.Invoke("archive-open");
            using var stream = File.OpenRead(path);
            var archiveHash = Convert.ToHexString(SHA256.HashData(stream));
            stream.Position = 0;
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing backup manifest.");
            using var reader = new StreamReader(entry.Open());
            var json = reader.ReadToEnd();
            var manifest = JsonSerializer.Deserialize<BackupManifest>(json) ?? throw new InvalidDataException("Invalid backup manifest.");
            if (manifest.SchemaVersion > CatalogMigrations.CurrentVersion)
                throw new NotSupportedException($"This backup needs a newer app supporting catalog schema {manifest.SchemaVersion} (created by {manifest.AppVersion}).");
            if (manifest.Damaged || manifest.CatalogIdentity == Guid.Empty || manifest.FormatVersion != 1 || manifest.Entries == null ||
                !manifest.Entries.ContainsKey("catalog.db") || !manifest.Entries.ContainsKey(".catalog-identity") ||
                zip.Entries.Count != manifest.Entries.Count + 1)
                throw new InvalidDataException("This backup is damaged or has an unsupported format.");
            if (zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
                throw new InvalidDataException("Duplicate backup entries.");
            foreach (var (name, expected) in manifest.Entries)
            {
                if (!SafeEntry(name)) throw new InvalidDataException("Unsafe backup entry.");
                var source = zip.GetEntry(name) ?? throw new InvalidDataException("Missing backup entry.");
                var target = System.IO.Path.Combine(temporary, name);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                source.ExtractToFile(target);
                if (source.Length != expected.Size || Hash(target) != expected.Sha256)
                    throw new InvalidDataException("Backup checksum mismatch; this backup is damaged.");
            }
            if (ReadIdentity(temporary) != manifest.CatalogIdentity)
                throw new InvalidDataException("Backup identity mismatch.");
            var validationRoot = temporary;
            var hasJournal = manifest.Entries.ContainsKey("catalog.db-journal");
            if (hasJournal)
            {
                // Recovery may modify both files; retain the hash-verified payload for the swap.
                validationRoot = System.IO.Path.Combine(temporary, "validation");
                Directory.CreateDirectory(validationRoot);
                foreach (var name in new[] { "catalog.db", "catalog.db-journal" })
                    File.Copy(System.IO.Path.Combine(temporary, name), System.IO.Path.Combine(validationRoot, name));
            }
            using (var copy = CatalogService.OpenBackupCopy(System.IO.Path.Combine(validationRoot, "catalog.db"),
                       hasJournal ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly))
            {
                using var check = copy.CreateCommand();
                check.CommandText = "PRAGMA integrity_check;";
                if (!Equals(check.ExecuteScalar(), "ok") ||
                    CatalogService.ReadBackupFacts(copy) != (manifest.ImageRows, manifest.SchemaVersion))
                    throw new InvalidDataException("Backup integrity check failed; this backup is damaged.");
            }
            return new(temporary, manifest, archiveHash,
                Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json))));
        }
        catch (Exception ex)
        {
            Directory.Delete(temporary, recursive: true);
            if (ex is InvalidDataException or JsonException ||
                ex is SqliteException { SqliteErrorCode: 11 or 26 }) MarkDamaged(path);
            throw;
        }
    }

    internal static bool SafeEntry(string name) => name is "catalog.db" or "catalog.db-journal" or ".catalog-identity" ||
        name.StartsWith("presets/", StringComparison.Ordinal) && name[8..].Length > 0 &&
        name[8..].IndexOfAny(['/', '\\', ':']) < 0 && name[8..] is not "." and not ".." &&
        name[8..].IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0 &&
        !name.EndsWith('.') && !name.EndsWith(' ');

    internal static Guid? ReadIdentity(string root)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(root, ".catalog-identity")));
            return doc.RootElement.GetProperty("catalogId").GetGuid();
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException) { return null; }
    }

    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal void RewriteSidecar(string path, BackupManifest manifest)
    {
        var sidecar = System.IO.Path.ChangeExtension(path, ".manifest.json");
        var temporary = sidecar + ".partial";
        try
        {
            var json = JsonSerializer.Serialize(manifest);
            try
            {
                if (File.Exists(sidecar) && !CloudOnly(sidecar) &&
                    JsonSerializer.Serialize(JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(sidecar))) == json) return;
            }
            catch (JsonException) { }
            Step?.Invoke("before:sidecar-write");
            File.WriteAllText(temporary, json);
            Step?.Invoke("after:sidecar-write");
            Step?.Invoke("before:sidecar-replace");
            File.Move(temporary, sidecar, overwrite: true);
            Step?.Invoke("after:sidecar-replace");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

internal sealed record BackupEntry(long Size, string Sha256);
internal sealed record BackupManifest(int FormatVersion, string Kind, DateTimeOffset Utc,
    string AppVersion, Guid CatalogIdentity, long SchemaVersion, long ImageRows,
    Dictionary<string, BackupEntry> Entries);
internal sealed record BackupOutcome(DateTimeOffset Utc, string Status, string? Reason = null);

public sealed class CatalogBackupService(CatalogService catalog)
{
    internal const string OutcomeKey = "backup_last_attempt";
    private readonly object _sync = new();
    private Task? _running;
    internal Action<string>? Step { get; set; }
    internal Func<DateTimeOffset> UtcNow { get; set; } = () => DateTimeOffset.UtcNow;
    internal Func<string, long> FreeSpace { get; set; } = path =>
        new DriveInfo(path).AvailableFreeSpace;
    internal string Folder => Path.Combine(catalog.CatalogPath, "Backups");

    public Task BackupIfDueAsync() => BackupAsync("scheduled", onlyIfDue: true);

    public Task BackupAsync(string kind = "manual", bool onlyIfDue = false)
    {
        lock (_sync)
            return _running is { IsCompleted: false } ? _running :
                _running = Task.Run(() => RunAsync(kind, onlyIfDue));
    }

    internal List<(string Path, BackupManifest Manifest)> List()
    {
        Step?.Invoke("list");
        if (!Directory.Exists(Folder)) return [];
        var names = Directory.GetFiles(Folder).ToHashSet(StringComparer.Ordinal);
        var result = new List<(string, BackupManifest)>();
        foreach (var path in names.Where(path => path.EndsWith(".manifest.json", StringComparison.Ordinal)))
        {
            var stem = path[..^14];
            if (!IsBackupName(Path.GetFileName(stem)) || !names.Contains(stem + ".zip")) continue;
            try
            {
                // Do not hydrate a sidecar during a silent schedule check.
                if (((int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000)) != 0) continue;
                var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(path));
                if (manifest is { FormatVersion: 1, Entries: not null } &&
                    manifest.CatalogIdentity == catalog.OpenCatalogIdentity &&
                    manifest.Entries.ContainsKey("catalog.db") && manifest.Entries.ContainsKey(".catalog-identity"))
                    result.Add((stem, manifest));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return result;
    }

    internal bool IsDue() => !List().Any(item => item.Manifest.Utc >= UtcNow().AddDays(-7));
    private static bool IsBackupName(string name) => name.StartsWith("hp-backup-", StringComparison.Ordinal) &&
        Guid.TryParseExact(name[10..], "N", out _);

    private async Task RunAsync(string kind, bool onlyIfDue)
    {
        if (catalog.OpenCatalogIdentity is not { } identity) return;
        BackupOutcome outcome;
        try
        {
            if (onlyIfDue && !IsDue()) return;
            AssertOwned();
            Directory.CreateDirectory(Folder);
            foreach (var path in Directory.GetFiles(Folder, "hp-backup-*.partial.*"))
                if (IsBackupName(Path.GetFileName(path).Split(".partial.")[0]))
                    Change("sweep", () => File.Delete(path));
            if (AvailableFreeSpace() < 2 * new FileInfo(Path.Combine(catalog.CatalogPath, "catalog.db")).Length)
                outcome = new(UtcNow(), "skipped-disk-space");
            else
            {
                await CreateAsync(identity, kind).ConfigureAwait(false);
                outcome = new(UtcNow(), "ok");
            }
        }
        catch (Exception ex)
        {
            outcome = new(UtcNow(), ex is CatalogDamagedException ||
                ex is SqliteException { SqliteErrorCode: 11 or 26 } ? "catalog-damaged" : "failed", ex.Message);
        }
        try
        {
            Step?.Invoke("before:outcome");
            await catalog.SetAppSettingAsync(OutcomeKey, JsonSerializer.Serialize(outcome)).ConfigureAwait(false);
            Step?.Invoke("after:outcome");
        }
        catch (Exception) { } // An unwritable/damaged catalog must still allow quit.
    }

    private async Task CreateAsync(Guid identity, string kind)
    {
        var stem = Path.Combine(Folder, $"hp-backup-{Guid.NewGuid():N}");
        var snapshot = stem + ".partial.db";
        var zipPath = stem + ".partial.zip";
        var sidecar = stem + ".partial.json";
        var facts = await catalog.SnapshotAsync(snapshot, Step).ConfigureAwait(false);
        using (var copy = CatalogService.OpenBackupCopy(snapshot, SqliteOpenMode.ReadOnly))
        {
            using var check = copy.CreateCommand();
            check.CommandText = "PRAGMA integrity_check;";
            if (!Equals(check.ExecuteScalar(), "ok")) throw new CatalogDamagedException();
            if (facts != CatalogService.ReadBackupFacts(copy)) throw new IOException("Snapshot metadata mismatch.");
        }
        var entries = new Dictionary<string, BackupEntry>();
        var smallFiles = new Dictionary<string, byte[]>
        {
            [".catalog-identity"] = File.ReadAllBytes(Path.Combine(catalog.CatalogPath, ".catalog-identity"))
        };
        using (var document = JsonDocument.Parse(smallFiles[".catalog-identity"]))
            if (document.RootElement.GetProperty("catalogId").GetGuid() != identity)
                throw new IOException("Catalog identity changed.");
        var presets = Path.Combine(catalog.CatalogPath, "presets");
        foreach (var path in Directory.Exists(presets) ? Directory.GetFiles(presets, "*.json") : [])
        {
            var bytes = await ReadPresetAsync(path).ConfigureAwait(false);
            if (bytes != null) smallFiles["presets/" + Path.GetFileName(path)] = bytes;
        }
        Step?.Invoke("before:zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var source = File.OpenRead(snapshot)) Add("catalog.db", source);
            foreach (var (name, bytes) in smallFiles)
            {
                using var source = new MemoryStream(bytes);
                Add(name, source);
            }
            var manifest = new BackupManifest(1, kind, UtcNow(), AppBuildInfo.Version.ToString(),
                identity, facts.Schema, facts.Rows, entries);
            using var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            writer.Write(JsonSerializer.Serialize(manifest));
            void Add(string name, Stream source)
            {
                entries[name] = new(source.Length, Convert.ToHexString(SHA256.HashData(source)));
                source.Position = 0;
                using var destination = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                source.CopyTo(destination);
            }
        }
        Step?.Invoke("after:zip");
        var verified = VerifyArchive(zipPath);
        Change("sidecar", () => File.WriteAllText(sidecar, JsonSerializer.Serialize(verified)));
        Change("publish-zip", () => File.Move(zipPath, stem + ".zip"));
        Change("publish-sidecar", () => File.Move(sidecar, stem + ".manifest.json"));
        Change("snapshot-delete", () => File.Delete(snapshot));
        foreach (var item in List().Where(item => item.Manifest.Kind is "scheduled" or "manual")
                     .OrderByDescending(item => item.Manifest.Utc).Skip(5))
        {
            Change("prune-sidecar", () => File.Delete(item.Path + ".manifest.json"));
            Change("prune-zip", () => File.Delete(item.Path + ".zip"));
        }
    }

    internal static BackupManifest VerifyArchive(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open());
        var manifest = JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd())!;
        if (zip.Entries.Count != manifest.Entries.Count + 1) throw new IOException("Unexpected backup entries.");
        foreach (var (name, expected) in manifest.Entries)
        {
            var entry = zip.GetEntry(name) ?? throw new IOException("Missing backup entry.");
            using var source = entry.Open();
            if (entry.Length != expected.Size || Convert.ToHexString(SHA256.HashData(source)) != expected.Sha256)
                throw new IOException("Backup checksum mismatch.");
        }
        return manifest;
    }

    private async Task<byte[]?> ReadPresetAsync(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Step?.Invoke("preset-open");
                using var bytes = new MemoryStream();
                await stream.CopyToAsync(bytes).ConfigureAwait(false);
                var result = bytes.ToArray();
                bytes.Position = 0;
                using var reader = new StreamReader(bytes, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var preset = PresetService.DeserializePresetFile(reader.ReadToEnd(), path);
                if (preset == null || string.IsNullOrWhiteSpace(preset.Id) || string.IsNullOrWhiteSpace(preset.Name))
                    return null;
                return result;
            }
            catch (JsonException) { return null; }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException) when (attempt < 2) { await Task.Delay(10).ConfigureAwait(false); }
        }
    }

    private long AvailableFreeSpace()
    {
        try { return FreeSpace(Folder); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return long.MaxValue; // Unknown capacity: let an actual write failure determine the outcome.
        }
    }

    private void AssertOwned()
    {
        AppDataRootOwnership.AssertAppOwned(catalog.CatalogPath);
        if (!AppDataRootOwnership.IsSameOrDescendant(AppDataRootOwnership.ResolveRealPath(catalog.CatalogPath),
                AppDataRootOwnership.ResolveRealPath(Folder))) throw new IOException("Backup folder escapes catalog root.");
    }

    private void Change(string name, Action action)
    {
        AssertOwned();
        Step?.Invoke("before:" + name);
        action();
        Step?.Invoke("after:" + name);
    }

    private sealed class CatalogDamagedException() : IOException("Snapshot integrity check failed.");
}

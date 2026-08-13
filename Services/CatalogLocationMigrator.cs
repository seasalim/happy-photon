using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public enum CatalogLocationMoveKind
{
    Catalog,
    Cache,
    SetAside
}

public enum CatalogLocationMovePhase
{
    Prepared,
    CatalogCopied,
    CacheMoved,
    Verified,
    ReplacementRootsCreated,
    PointerFlipped
}

public sealed class CatalogLocationMigrator
{
    private const string JournalFileName = "location-move.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private readonly AppDataLocationService _locations;
    private readonly Action<CatalogLocationMovePhase>? _afterCommit;

    public CatalogLocationMigrator(AppDataLocationService locations)
        : this(locations, null)
    {
    }

    internal CatalogLocationMigrator(
        AppDataLocationService locations,
        Action<CatalogLocationMovePhase>? afterCommit)
    {
        _locations = locations;
        _afterCommit = afterCommit;
    }

    public string JournalPath => Path.Combine(
        Path.GetDirectoryName(_locations.PointerPath)!, JournalFileName);

    public async Task StageMoveAsync(
        AppDataLocations current,
        CatalogLocationMoveKind kind,
        string? destinationRoot = null)
    {
        if (File.Exists(JournalPath))
        {
            throw new InvalidOperationException("A storage move is already pending.");
        }
        if (current.IsCatalogEnvironmentManaged || current.IsCacheEnvironmentManaged)
        {
            throw new InvalidOperationException(
                "Remove or repoint the storage environment variable before moving this location.");
        }

        var destination = destinationRoot == null
            ? null
            : Path.GetFullPath(destinationRoot);
        if (kind != CatalogLocationMoveKind.SetAside)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destination);
            AppDataRootOwnership.AssertAppOwned(destination!);
            AssertDestinationEmpty(destination!);
            var proposed = kind == CatalogLocationMoveKind.Catalog
                ? current.WithCatalog(destination!)
                : current.WithCache(destination!);
            AppDataRootOwnership.ValidateProposedRoots(
                proposed.CatalogRoot,
                proposed.CacheRoot,
                current.Topology == AppDataLocationTopology.LegacyCoLocated
                    ? []
                    : [kind == CatalogLocationMoveKind.Catalog
                        ? current.CatalogRoot
                        : current.CacheRoot]);
        }

        await CommitAsync(new MoveJournal(
            1,
            kind,
            CatalogLocationMovePhase.Prepared,
            current.CatalogRoot,
            current.CacheRoot,
            current.CatalogOrigin,
            current.CacheOrigin,
            destination,
            null,
            null,
            null));
    }

    public async Task ExecutePendingAsync()
    {
        if (!File.Exists(JournalPath)) return;
        var journal = await ReadJournalAsync();
        try
        {
            if (journal.Kind == CatalogLocationMoveKind.SetAside)
            {
                await ExecuteSetAsideAsync(journal);
            }
            else
            {
                await ExecuteMoveAsync(journal);
            }
        }
        catch
        {
            journal = await ReadJournalAsync();
            var mustResumeSetAside =
                journal.Kind == CatalogLocationMoveKind.SetAside &&
                journal.Phase >= CatalogLocationMovePhase.ReplacementRootsCreated;
            if (journal.Phase < CatalogLocationMovePhase.PointerFlipped &&
                !mustResumeSetAside)
            {
                RollBack(journal);
                AppDataRootOwnership.AssertAppOwned(
                    Path.GetDirectoryName(_locations.PointerPath)!);
                File.Delete(JournalPath);
            }
            throw;
        }
    }

    private async Task ExecuteMoveAsync(MoveJournal journal)
    {
        var destination = journal.DestinationRoot!;
        if (journal.Kind == CatalogLocationMoveKind.Catalog &&
            journal.Phase == CatalogLocationMovePhase.Prepared)
        {
            var fingerprint = await FingerprintAsync(journal.CatalogRoot);
            CopyCatalog(journal.CatalogRoot, destination);
            journal = await CommitAsync(journal with
            {
                Phase = CatalogLocationMovePhase.CatalogCopied,
                Fingerprint = fingerprint
            });
        }
        if (journal.Kind == CatalogLocationMoveKind.Cache &&
            journal.Phase == CatalogLocationMovePhase.Prepared)
        {
            if (journal.CacheWasRenamed == null)
            {
                journal = await CommitAsync(journal with
                {
                    CacheWasRenamed = ShouldRenameCache(
                        journal.CacheRoot, destination)
                });
            }
            if (journal.CacheWasRenamed == true)
                MoveCacheOrResume(journal.CacheRoot, destination);
            journal = await AdvanceAsync(journal, CatalogLocationMovePhase.CacheMoved);
        }
        if (journal.Kind == CatalogLocationMoveKind.Catalog &&
            journal.Phase == CatalogLocationMovePhase.CatalogCopied)
        {
            await VerifyCatalogAsync(destination, journal.Fingerprint!);
            journal = await AdvanceAsync(journal, CatalogLocationMovePhase.Verified);
        }
        if (journal.Phase < CatalogLocationMovePhase.PointerFlipped)
        {
            var next = journal.Kind == CatalogLocationMoveKind.Catalog
                ? new AppDataLocations(
                    destination,
                    journal.CacheRoot,
                    AppDataLocationOrigin.Persisted,
                    journal.CacheOrigin)
                : new AppDataLocations(
                    journal.CatalogRoot,
                    destination,
                    journal.CatalogOrigin,
                    AppDataLocationOrigin.Persisted);
            await _locations.PersistAsync(next);
            journal = await AdvanceAsync(journal, CatalogLocationMovePhase.PointerFlipped);
        }

        CleanSource(journal);
        DeleteJournal();
    }

    private async Task ExecuteSetAsideAsync(MoveJournal journal)
    {
        if (journal.Phase == CatalogLocationMovePhase.Prepared)
        {
            if (journal.CacheAsideRoot == null ||
                journal.CatalogAsideRoot == null)
            {
                var suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
                var cacheAside = $"{journal.CacheRoot}.set-aside-{suffix}";
                journal = await CommitAsync(journal with
                {
                    CacheAsideRoot = cacheAside,
                    CatalogAsideRoot = Same(journal.CatalogRoot, journal.CacheRoot)
                        ? cacheAside
                        : $"{journal.CatalogRoot}.set-aside-{suffix}"
                });
            }
            MoveAsideOrResume(journal.CacheRoot, journal.CacheAsideRoot!);
            journal = await AdvanceAsync(journal, CatalogLocationMovePhase.CacheMoved);
        }
        if (journal.Phase == CatalogLocationMovePhase.CacheMoved)
        {
            var sameRoot = Same(journal.CatalogRoot, journal.CacheRoot);
            if (!sameRoot)
            {
                MoveAsideOrResume(
                    journal.CatalogRoot,
                    journal.CatalogAsideRoot!);
            }
            journal = await AdvanceAsync(journal, CatalogLocationMovePhase.Verified);
        }
        if (journal.Phase == CatalogLocationMovePhase.Verified)
        {
            journal = await AdvanceAsync(
                journal, CatalogLocationMovePhase.ReplacementRootsCreated);
        }
        if (journal.Phase == CatalogLocationMovePhase.ReplacementRootsCreated)
        {
            await _locations.CreateFreshAfterSetAsideAsync();
            journal = await AdvanceAsync(journal, CatalogLocationMovePhase.PointerFlipped);
        }
        DeleteJournal();
    }
    private static bool ShouldRenameCache(string source, string destination)
    {
        var sourceAssets = Path.Combine(source, "assets");
        if (!Directory.Exists(sourceAssets)) return false;
        if (!CanRenameBetween(source, destination)) return false;
        var destinationAssets = Path.Combine(destination, "assets");
        if (Directory.Exists(destinationAssets) &&
            Directory.EnumerateFileSystemEntries(destinationAssets).Any())
        {
            throw new IOException("The destination cache is not empty.");
        }
        return true;
    }
    private static void MoveCacheOrResume(string source, string destination)
    {
        var sourceAssets = Path.Combine(source, "assets");
        var destinationAssets = Path.Combine(destination, "assets");
        if (!Directory.Exists(sourceAssets))
        {
            if (!Directory.Exists(destinationAssets)) throw new IOException(
                "The cache move cannot be resumed.");
            AppDataRootOwnership.AssertAppOwned(destination);
            return;
        }
        if (Directory.Exists(destinationAssets))
        {
            if (Directory.EnumerateFileSystemEntries(destinationAssets).Any())
                throw new IOException("The destination cache is not empty.");
            AppDataRootOwnership.AssertAppOwned(destination);
            Directory.Delete(destinationAssets);
        }
        AppDataRootOwnership.AssertAppOwned(source);
        Directory.Move(sourceAssets, destinationAssets);
    }
    private static void MoveAsideOrResume(string source, string aside)
    {
        if (Directory.Exists(source))
        {
            if (Directory.Exists(aside))
                throw new IOException("The set-aside destination already exists.");
            AppDataRootOwnership.AssertAppOwned(source);
            Directory.Move(source, aside);
            return;
        }
        if (!Directory.Exists(aside)) throw new IOException(
            "The set-aside operation cannot be resumed.");
        AppDataRootOwnership.AssertAppOwned(aside);
    }

    private static void CopyCatalog(string source, string destination)
    {
        AppDataRootOwnership.AssertAppOwned(source);
        AppDataRootOwnership.AssertAppOwned(destination);
        CopyFile(source, destination, "catalog.db");
        CopyFile(source, destination, ".catalog-identity");
        var sourcePresets = Path.Combine(source, "presets");
        var destinationPresets = Path.Combine(destination, "presets");
        Directory.CreateDirectory(destinationPresets);
        if (!Directory.Exists(sourcePresets)) return;
        foreach (var preset in Directory.EnumerateFiles(sourcePresets, "*.json"))
        {
            File.Copy(preset, Path.Combine(destinationPresets, Path.GetFileName(preset)), true);
        }
    }

    private static void CopyFile(string source, string destination, string name)
    {
        var path = Path.Combine(source, name);
        if (!File.Exists(path)) throw new FileNotFoundException(name, path);
        File.Copy(path, Path.Combine(destination, name), true);
    }

    private static async Task<CatalogFingerprint> FingerprintAsync(string root)
    {
        var database = Path.Combine(root, "catalog.db");
        var presets = Path.Combine(root, "presets");
        return new CatalogFingerprint(
            await RecoverAndCountRowsAsync(database),
            HashFile(Path.Combine(root, ".catalog-identity")),
            Directory.Exists(presets)
                ? Directory.EnumerateFiles(presets, "*.json")
                    .ToDictionary(
                        path => Path.GetFileName(path),
                        HashFile,
                        StringComparer.Ordinal)
                : new Dictionary<string, string>());
    }

    private static async Task VerifyCatalogAsync(
        string root,
        CatalogFingerprint expected)
    {
        var actual = await FingerprintAsync(root);
        if (actual.RowCount != expected.RowCount ||
            actual.IdentityHash != expected.IdentityHash ||
            actual.Presets.Count != expected.Presets.Count ||
            expected.Presets.Any(pair =>
                !actual.Presets.TryGetValue(pair.Key, out var hash) || hash != pair.Value))
        {
            throw new IOException("The copied catalog did not verify.");
        }
    }

    private static async Task<long> RecoverAndCountRowsAsync(string database)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={database};Mode=ReadWrite;Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM images;";
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void CleanSource(MoveJournal journal)
    {
        if (journal.Kind == CatalogLocationMoveKind.Catalog)
        {
            DeleteFile(journal.CatalogRoot, "catalog.db");
            DeleteFile(journal.CatalogRoot, ".catalog-identity");
            DeleteKnownDirectory(journal.CatalogRoot, "presets");
        }
        else if (journal.CacheWasRenamed != true)
        {
            CatalogCacheStamp.ClearTiers(
                journal.CacheRoot,
                Path.Combine(journal.CacheRoot, "assets"));
        }
    }

    private static void RollBack(MoveJournal journal)
    {
        if (journal.Kind == CatalogLocationMoveKind.Catalog &&
            journal.DestinationRoot != null)
        {
            DeleteFile(journal.DestinationRoot, "catalog.db");
            DeleteFile(journal.DestinationRoot, ".catalog-identity");
            DeleteKnownDirectory(journal.DestinationRoot, "presets");
        }
        if (journal.Kind == CatalogLocationMoveKind.Cache &&
            journal.CacheWasRenamed == true && journal.DestinationRoot != null)
        {
            var assets = Path.Combine(journal.DestinationRoot, "assets");
            if (Directory.Exists(assets))
            {
                AppDataRootOwnership.AssertAppOwned(journal.DestinationRoot);
                Directory.Move(assets, Path.Combine(journal.CacheRoot, "assets"));
            }
        }
        if (journal.Kind == CatalogLocationMoveKind.SetAside)
        {
            if (Same(journal.CatalogRoot, journal.CacheRoot))
            {
                RestoreAside(
                    journal.CatalogAsideRoot ?? journal.CacheAsideRoot,
                    journal.CatalogRoot);
            }
            else
            {
                RestoreAside(journal.CatalogAsideRoot, journal.CatalogRoot);
                RestoreAside(journal.CacheAsideRoot, journal.CacheRoot);
            }
        }
    }

    private static void RestoreAside(string? aside, string root)
    {
        if (aside == null || !Directory.Exists(aside) || Directory.Exists(root)) return;
        AppDataRootOwnership.AssertAppOwned(aside);
        Directory.Move(aside, root);
    }

    private static void DeleteFile(string root, string relative)
    {
        var path = Path.Combine(root, relative);
        if (!File.Exists(path)) return;
        AppDataRootOwnership.AssertAppOwned(root);
        File.Delete(path);
    }

    private static void DeleteKnownDirectory(string root, string relative)
    {
        var path = Path.Combine(root, relative);
        if (!Directory.Exists(path)) return;
        AppDataRootOwnership.AssertAppOwned(root);
        Directory.Delete(path, recursive: true);
    }

    private async Task<MoveJournal> AdvanceAsync(
        MoveJournal journal,
        CatalogLocationMovePhase phase) =>
        await CommitAsync(journal with { Phase = phase });

    private Task<MoveJournal> CommitAsync(MoveJournal journal)
    {
        var pointerRoot = Path.GetDirectoryName(_locations.PointerPath)!;
        AppDataRootOwnership.Claim(pointerRoot);
        AppDataRootOwnership.WriteAtomicOwned(
            pointerRoot,
            JournalPath,
            JsonSerializer.Serialize(journal, JsonOptions));
        _afterCommit?.Invoke(journal.Phase);
        return Task.FromResult(journal);
    }

    private async Task<MoveJournal> ReadJournalAsync() =>
        JsonSerializer.Deserialize<MoveJournal>(
            await File.ReadAllTextAsync(JournalPath), JsonOptions) ??
        throw new InvalidDataException("The storage move journal is invalid.");

    private void DeleteJournal()
    {
        if (!File.Exists(JournalPath)) return;
        AppDataRootOwnership.AssertAppOwned(
            Path.GetDirectoryName(_locations.PointerPath)!);
        File.Delete(JournalPath);
    }

    // Path.GetPathRoot is "/" for every Unix mount, so volume identity must be
    // probed with a real rename rather than compared by root string.
    private static bool CanRenameBetween(string source, string destination)
    {
        if (!string.Equals(
                Path.GetPathRoot(Path.GetFullPath(source)),
                Path.GetPathRoot(Path.GetFullPath(destination)),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var probe = Path.Combine(source, $".move-probe-{Guid.NewGuid():N}");
        var target = Path.Combine(destination, $".move-probe-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(probe);
            Directory.Move(probe, target);
            Directory.Delete(target);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(probe)) Directory.Delete(probe);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool Same(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static void AssertDestinationEmpty(string destination)
    {
        var unexpected = Directory.EnumerateFileSystemEntries(destination)
            .Any(path => !string.Equals(
                Path.GetFileName(path),
                AppDataRootOwnership.MarkerFileName,
                StringComparison.Ordinal));
        if (unexpected)
        {
            throw new IOException(
                "The dedicated destination folder must be empty before a move is staged.");
        }
    }

    private sealed record CatalogFingerprint(
        long RowCount,
        string IdentityHash,
        Dictionary<string, string> Presets);

    private sealed record MoveJournal(
        int Version,
        CatalogLocationMoveKind Kind,
        CatalogLocationMovePhase Phase,
        string CatalogRoot,
        string CacheRoot,
        AppDataLocationOrigin CatalogOrigin,
        AppDataLocationOrigin CacheOrigin,
        string? DestinationRoot,
        CatalogFingerprint? Fingerprint,
        bool? CacheWasRenamed,
        string? CacheAsideRoot,
        string? CatalogAsideRoot = null);
}

using System.Text.Json;

namespace HappyPhoton.Services;

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
            CatalogMoveFileOperations.AssertDestinationEmpty(destination!);
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

        await CommitAsync(new CatalogLocationMoveJournal(
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

    private async Task ExecuteMoveAsync(CatalogLocationMoveJournal journal)
    {
        var destination = journal.DestinationRoot!;
        if (journal.Kind == CatalogLocationMoveKind.Catalog &&
            journal.Phase == CatalogLocationMovePhase.Prepared)
        {
            var fingerprint = await CatalogFingerprinter.FingerprintAsync(
                journal.CatalogRoot);
            CatalogMoveFileOperations.CopyCatalog(journal.CatalogRoot, destination);
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
                    CacheWasRenamed = CatalogMoveFileOperations.ShouldRenameCache(
                        journal.CacheRoot, destination)
                });
            }
            if (journal.CacheWasRenamed == true)
                CatalogMoveFileOperations.MoveCacheOrResume(journal.CacheRoot, destination);
            journal = await AdvanceAsync(journal, CatalogLocationMovePhase.CacheMoved);
        }
        if (journal.Kind == CatalogLocationMoveKind.Catalog &&
            journal.Phase == CatalogLocationMovePhase.CatalogCopied)
        {
            await CatalogFingerprinter.VerifyAsync(destination, journal.Fingerprint!);
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

    private async Task ExecuteSetAsideAsync(CatalogLocationMoveJournal journal)
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
                    CatalogAsideRoot = CatalogMoveFileOperations.SamePath(
                        journal.CatalogRoot, journal.CacheRoot)
                        ? cacheAside
                        : $"{journal.CatalogRoot}.set-aside-{suffix}"
                });
            }
            CatalogMoveFileOperations.MoveAsideOrResume(
                journal.CacheRoot, journal.CacheAsideRoot!);
            journal = await AdvanceAsync(journal, CatalogLocationMovePhase.CacheMoved);
        }
        if (journal.Phase == CatalogLocationMovePhase.CacheMoved)
        {
            var sameRoot = CatalogMoveFileOperations.SamePath(
                journal.CatalogRoot, journal.CacheRoot);
            if (!sameRoot)
            {
                CatalogMoveFileOperations.MoveAsideOrResume(
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

    private static void CleanSource(CatalogLocationMoveJournal journal)
    {
        if (journal.Kind == CatalogLocationMoveKind.Catalog)
        {
            CatalogMoveFileOperations.DeleteFile(journal.CatalogRoot, "catalog.db");
            CatalogMoveFileOperations.DeleteFile(journal.CatalogRoot, ".catalog-identity");
            CatalogMoveFileOperations.DeleteKnownDirectory(journal.CatalogRoot, "presets");
        }
        else if (journal.CacheWasRenamed != true)
        {
            CatalogCacheStamp.ClearTiers(
                journal.CacheRoot,
                Path.Combine(journal.CacheRoot, "assets"));
        }
    }

    private static void RollBack(CatalogLocationMoveJournal journal)
    {
        if (journal.Kind == CatalogLocationMoveKind.Catalog &&
            journal.DestinationRoot != null)
        {
            CatalogMoveFileOperations.DeleteFile(
                journal.DestinationRoot, "catalog.db");
            CatalogMoveFileOperations.DeleteFile(
                journal.DestinationRoot, ".catalog-identity");
            CatalogMoveFileOperations.DeleteKnownDirectory(
                journal.DestinationRoot, "presets");
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
            if (CatalogMoveFileOperations.SamePath(
                    journal.CatalogRoot, journal.CacheRoot))
            {
                CatalogMoveFileOperations.RestoreAside(
                    journal.CatalogAsideRoot ?? journal.CacheAsideRoot,
                    journal.CatalogRoot);
            }
            else
            {
                CatalogMoveFileOperations.RestoreAside(
                    journal.CatalogAsideRoot, journal.CatalogRoot);
                CatalogMoveFileOperations.RestoreAside(
                    journal.CacheAsideRoot, journal.CacheRoot);
            }
        }
    }

    private async Task<CatalogLocationMoveJournal> AdvanceAsync(
        CatalogLocationMoveJournal journal,
        CatalogLocationMovePhase phase) =>
        await CommitAsync(journal with { Phase = phase });

    private Task<CatalogLocationMoveJournal> CommitAsync(CatalogLocationMoveJournal journal)
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

    private async Task<CatalogLocationMoveJournal> ReadJournalAsync() =>
        JsonSerializer.Deserialize<CatalogLocationMoveJournal>(
            await File.ReadAllTextAsync(JournalPath), JsonOptions) ??
        throw new InvalidDataException("The storage move journal is invalid.");

    private void DeleteJournal()
    {
        if (!File.Exists(JournalPath)) return;
        AppDataRootOwnership.AssertAppOwned(
            Path.GetDirectoryName(_locations.PointerPath)!);
        File.Delete(JournalPath);
    }
}

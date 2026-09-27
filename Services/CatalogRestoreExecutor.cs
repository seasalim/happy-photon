using System.Text.Json;

namespace HappyPhoton.Services;

public sealed class CatalogRestoreExecutor(CatalogLocationMigrator journalStore)
{
    internal Action<string>? Step { get; set; }
    internal static string NoticePath(string root) => Path.Combine(root, "restore-notice.json");

    internal static string? ReadNotice(string root) => File.Exists(NoticePath(root))
        ? JsonSerializer.Deserialize<string>(File.ReadAllText(NoticePath(root))) : null;
    internal static void AcknowledgeNotice(string root) =>
        File.Delete(OwnedPath(root, "restore-notice.json"));

    public Task StageAsync(AppDataLocations locations, string path, bool acknowledgeDifferentCatalog = false) =>
        StageAsync(locations, path, null, acknowledgeDifferentCatalog);

    // Reuses only checked metadata; the chooser owns the disposable extraction directory.
    internal Task StageAsync(AppDataLocations locations, string path, CheckedCatalogBackup? checkedBackup,
        bool acknowledgeDifferentCatalog) => Task.Run(async () =>
        {
            if (File.Exists(journalStore.JournalPath))
                throw new InvalidOperationException("A storage move or restore is already pending.");
            using var catalog = new CatalogService(locations.CatalogRoot);
            var service = new CatalogBackupService(catalog) { Step = Step };
            using var ownedCheck = checkedBackup == null ? service.CheckForRestore(path) : null;
            checkedBackup ??= ownedCheck!;
            AssertIdentity(locations.CatalogRoot, checkedBackup.Manifest, acknowledgeDifferentCatalog);
            AppDataRootOwnership.AssertAppOwned(locations.CatalogRoot);
            AssertCacheOwnedIfPresent(locations.CacheRoot);
            var state = new CatalogRestoreState(Path.GetFullPath(path), checkedBackup.ArchiveHash,
                checkedBackup.ManifestHash, $"hp-backup-{Guid.NewGuid():N}", acknowledgeDifferentCatalog);
            // Only an accepted check may repair a copied-in archive's metadata.
            service.RewriteSidecar(path, checkedBackup.Manifest);
            await Commit(new(1, CatalogLocationMoveKind.Restore, CatalogLocationMovePhase.Prepared,
                locations.CatalogRoot, locations.CacheRoot, locations.CatalogOrigin, locations.CacheOrigin,
                null, null, null, null, Restore: state));
        });

    internal async Task ExecuteAsync(CatalogLocationMoveJournal journal)
    {
        var state = journal.Restore ?? throw new IOException("Missing restore journal data.");
        if (!state.BeforeStem.StartsWith("hp-backup-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(state.BeforeStem[10..], "N", out _))
            throw new IOException("Invalid restore preservation name.");
        var root = journal.CatalogRoot;
        var staging = Path.Combine(root, ".restore-" + state.BeforeStem);
        if (state.Phase != CatalogRestorePhase.Prepared) AssertRoots();
        if (state.Phase is > CatalogRestorePhase.Prepared and < CatalogRestorePhase.Replacing)
        {
            Cleanup();
            return; // A previous process never committed to replacing the originals.
        }
        if (state.Phase == CatalogRestorePhase.Prepared)
        {
            try
            {
                AssertRoots();
                using var catalog = new CatalogService(root);
                var service = new CatalogBackupService(catalog) { Step = Step };
                using var backup = service.CheckForRestore(state.BackupPath);
                if (backup.ArchiveHash != state.ArchiveHash || backup.ManifestHash != state.ManifestHash)
                {
                    CatalogBackupService.MarkDamaged(state.BackupPath);
                    throw new IOException("The staged backup changed; restore refused.");
                }
                AssertIdentity(root, backup.Manifest, state.AcknowledgeDifferentCatalog);
                await Advance(CatalogRestorePhase.Verified);
                AppDataRootOwnership.ClaimFresh(staging);
                foreach (var (name, _) in backup.Manifest.Entries)
                {
                    var target = OwnedPath(staging, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    Change("stage:" + name, () => File.Copy(Path.Combine(backup.Directory, name), target));
                }
                state = state with { Entries = backup.Manifest.Entries };
                await Advance(CatalogRestorePhase.PayloadStaged);
                CatalogRestorePreservation.Create(root, staging, Path.Combine(service.Folder, state.BeforeStem), Change);
                await Advance(CatalogRestorePhase.Preserved);
                await Advance(CatalogRestorePhase.Replacing);
            }
            catch
            {
                if (state.Phase == CatalogRestorePhase.Prepared) journalStore.DeleteJournal();
                throw;
            }
        }
        if (state.Phase == CatalogRestorePhase.Replacing)
        {
            var names = new HashSet<string>(state.Entries!.Keys, File.Exists(Path.Combine(root,
                AppDataRootOwnership.MarkerFileName.ToUpperInvariant())) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var (name, expected) in state.Entries!)
            {
                if (!CatalogBackupService.SafeEntry(name)) throw new IOException("Unsafe staged file.");
                var target = OwnedPath(root, name);
                var source = OwnedPath(staging, name);
                var existing = File.Exists(target) ? Directory.EnumerateFiles(Path.GetDirectoryName(target)!)
                    .First(file => names.Comparer.Equals(Path.GetFileName(file), Path.GetFileName(target))) : null;
                var matches = existing != null && CatalogBackupService.Hash(existing) == expected.Sha256;
                if (matches && Path.GetFileName(existing) == Path.GetFileName(target)) continue;
                // Keep a recoverable copy while correcting a case-only name, even after a prior rename.
                if (matches) Change("case-stage:" + name, () => File.Move(existing!, source, overwrite: true));
                if (CatalogBackupService.Hash(source) != expected.Sha256)
                    throw new IOException("The local restore payload is damaged.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (existing != null && !matches && Path.GetFileName(existing) != Path.GetFileName(target))
                    Change("case-delete:" + name, () => File.Delete(existing));
                Change("replace:" + name, () => File.Move(source, target, overwrite: true));
            }
            foreach (var name in CatalogRestorePreservation.Files(root).Where(name =>
                         !names.Contains(name)).Concat(["catalog.db-wal", "catalog.db-shm"]).Distinct())
            {
                var target = OwnedPath(root, name);
                if (File.Exists(target)) Change("delete:" + name, () => File.Delete(target));
            }
            await Advance(CatalogRestorePhase.CacheReset);
        }
        if (state.Phase == CatalogRestorePhase.CacheReset)
        {
            if (!Directory.Exists(journal.CacheRoot))
                Change("cache-create", () => AppDataRootOwnership.ClaimFresh(journal.CacheRoot));
            CatalogCacheStamp.ClearTiers(journal.CacheRoot, Path.Combine(journal.CacheRoot, "assets"), (name, action) =>
            {
                OwnedPath(journal.CacheRoot, "assets/" + name);
                Change("cache:" + name, action);
            });
            var stamp = OwnedPath(journal.CacheRoot, "assets/.catalog-stamp");
            if (File.Exists(stamp)) Change("cache-stamp", () => File.Delete(stamp));
            await Advance(CatalogRestorePhase.NoticeRecorded);
        }
        if (state.Phase == CatalogRestorePhase.NoticeRecorded)
        {
            using var catalog = new CatalogService(root);
            var preserved = Path.Combine(new CatalogBackupService(catalog).Folder, state.BeforeStem + ".zip");
            Change("notice", () => AppDataRootOwnership.WriteAtomicOwned(root, NoticePath(root),
                JsonSerializer.Serialize($"Restored {state.BackupPath}. The replaced catalog is preserved at {preserved}. Thumbnails will rebuild.")));
            Cleanup();
        }

        async Task Advance(CatalogRestorePhase phase)
        {
            state = state with { Phase = phase };
            journal = await Commit(journal with { Restore = state });
        }
        void Cleanup()
        {
            using var catalog = new CatalogService(root);
            CatalogRestorePreservation.CleanupPartials(root,
                Path.Combine(new CatalogBackupService(catalog).Folder, state.BeforeStem), Change);
            CatalogRestorePreservation.CleanupStaging(root, staging, Change);
            Change("journal-delete", journalStore.DeleteJournal);
        }
        void AssertRoots()
        {
            AppDataRootOwnership.AssertAppOwned(root);
            AssertCacheOwnedIfPresent(journal.CacheRoot);
        }
        void Change(string name, Action action)
        {
            AssertRoots();
            Step?.Invoke("before:" + name);
            action();
            Step?.Invoke("after:" + name);
        }
    }

    private static void AssertCacheOwnedIfPresent(string root)
    {
        if (Directory.Exists(root) || File.Exists(root)) AppDataRootOwnership.AssertAppOwned(root);
    }

    private async Task<CatalogLocationMoveJournal> Commit(CatalogLocationMoveJournal journal)
    {
        Step?.Invoke("before:commit:" + journal.Restore!.Phase);
        var result = await journalStore.CommitAsync(journal);
        Step?.Invoke("after:commit:" + journal.Restore!.Phase);
        return result;
    }

    internal static void AssertIdentity(string root, BackupManifest manifest, bool acknowledged)
    {
        if (CatalogBackupService.ReadIdentity(root) != manifest.CatalogIdentity && !acknowledged)
            throw new InvalidOperationException("This backup belongs to a different catalog. Explicit acknowledgement is required.");
    }

    internal static string OwnedPath(string root, string relative)
    {
        AppDataRootOwnership.AssertAppOwned(root);
        var path = Path.Combine(root, relative);
        if (!AppDataRootOwnership.IsSameOrDescendant(AppDataRootOwnership.ResolveRealPath(root),
                AppDataRootOwnership.ResolveRealPath(path)) ||
            File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Restore path escapes its owned root.");
        return path;
    }
}

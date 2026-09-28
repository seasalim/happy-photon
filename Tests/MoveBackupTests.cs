using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MoveBackupTests
{
    [Theory]
    [InlineData(false, CatalogLocationMovePhase.CatalogCopied)]
    [InlineData(true, CatalogLocationMovePhase.CatalogCopied)]
    [InlineData(false, CatalogLocationMovePhase.Verified)]
    [InlineData(true, CatalogLocationMovePhase.Verified)]
    public async Task ChangedOrMissingDestination_FailsBeforeFlip_SourceUntouched(
        bool remove, CatalogLocationMovePhase failurePhase)
    {
        using var directory = new TemporaryDirectory();
        var f = await MoveBackupTestSupport.CreateAsync(directory.Path);
        var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        var expected = MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot);
        var store = new CatalogLocationMigrator(f.Service, phase =>
        {
            if (phase != failurePhase) return;

            var path = CatalogMoveBackupFiles.PathFor(f.Destination, "archive-3.zip");

            if (remove)
            {
                File.Delete(path);
            }
            else
            {
                File.WriteAllText(path, "changed");
            }
        });
        await store.StageMoveAsync(f.Locations, CatalogLocationMoveKind.Catalog, f.Destination);
        await Assert.ThrowsAnyAsync<IOException>(store.ExecutePendingAsync);
        Assert.Equal(f.Locations.CatalogRoot, (await f.Service.ResolveAsync())!.CatalogRoot);
        Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot));
        Assert.Equal(original, RestoreTestSupport.Generation(f.Locations.CatalogRoot));
        Assert.DoesNotContain(Directory.GetFileSystemEntries(f.Destination),
            path => Path.GetFileName(path) != AppDataRootOwnership.MarkerFileName);
    }

    [Fact]
    public async Task AfterFlip_OnlyRecordedFilesAreRemoved()
    {
        using var directory = new TemporaryDirectory();
        var f = await MoveBackupTestSupport.CreateAsync(directory.Path);
        var expected = MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot);
        var store = new CatalogLocationMigrator(f.Service, phase =>
        {
            if (phase != CatalogLocationMovePhase.PointerFlipped) return;

            Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Destination));
            File.WriteAllText(CatalogMoveBackupFiles.PathFor(f.Locations.CatalogRoot, "later.zip"), "keep");
            throw new IOException("interrupt cleanup");
        });
        await store.StageMoveAsync(f.Locations, CatalogLocationMoveKind.Catalog, f.Destination);
        await Assert.ThrowsAsync<IOException>(store.ExecutePendingAsync);
        await new CatalogLocationMigrator(f.Service).ExecutePendingAsync();
        Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Destination));
        Assert.Equal("later.zip", Assert.Single(MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot)).Key);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloudOnly_RefusesAtStagingOrExecution_WithoutOpening(bool afterStaging)
    {
        using var directory = new TemporaryDirectory();
        var f = await MoveBackupTestSupport.CreateAsync(directory.Path);
        var expected = MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot);
        var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        var store = new CatalogLocationMigrator(f.Service);
        var cloud = CatalogMoveBackupFiles.PathFor(f.Locations.CatalogRoot, "archive-4.zip");

        if (afterStaging)
            await store.StageMoveAsync(f.Locations, CatalogLocationMoveKind.Catalog, f.Destination);

        store.BackupAttributes = path => path == cloud ? FileAttributes.Offline : File.GetAttributes(path);
        using (var locked = new FileStream(cloud, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var failure = await Assert.ThrowsAsync<IOException>(() => afterStaging
                ? store.ExecutePendingAsync()
                : store.StageMoveAsync(f.Locations, CatalogLocationMoveKind.Catalog, f.Destination));
            Assert.Contains("cloud-only", failure.Message);
        }

        Assert.Equal(original, RestoreTestSupport.Generation(f.Locations.CatalogRoot));
        Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot));
        Assert.False(File.Exists(store.JournalPath));
    }
}

internal sealed record MoveBackupFixture(AppDataLocationService Service, AppDataLocations Locations, string Destination);

internal static class MoveBackupTestSupport
{
    internal static async Task<MoveBackupFixture> CreateAsync(string root, BackupCatalogFixture? archive = null)
    {
        var f = await RestoreTestSupport.CreateAsync(root, archive);
        using var catalog = new CatalogService(f.Locations.CatalogRoot);
        var folder = new CatalogBackupService(catalog).Folder;
        var first = Path.Combine(folder, "archive-0.zip");
        File.Move(f.Backup, first);
        File.Move(Path.ChangeExtension(f.Backup, ".manifest.json"), Path.ChangeExtension(first, ".manifest.json"));

        for (var i = 1; i < 5; i++)
        {
            var next = Path.Combine(folder, $"archive-{i}.zip");
            File.Copy(first, next);

            if (i < 4)
                File.Copy(Path.ChangeExtension(first, ".manifest.json"), Path.ChangeExtension(next, ".manifest.json"));
        }

        File.WriteAllText(Path.Combine(folder, "damaged.zip"), "carry even damaged archives");
        var destination = Path.Combine(root, "destination");
        AppDataRootOwnership.ClaimFresh(destination);

        return new(f.Service, f.Locations, destination);
    }

    internal static SortedDictionary<string, string> Hashes(string root) => new(
        CatalogMoveBackupFiles.LocalFiles(root, File.GetAttributes).ToDictionary(name => name,
            name => CatalogBackupService.Hash(CatalogMoveBackupFiles.PathFor(root, name))), StringComparer.Ordinal);
}

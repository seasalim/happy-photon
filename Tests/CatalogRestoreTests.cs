using System.IO.Compression;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogRestoreTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ReplacesCompleteGeneration_PreservesBytes_ResetsCache(bool corrupt)
    {
        using var directory = new TemporaryDirectory();
        var fixture = await RestoreTestSupport.CreateAsync(directory.Path);
        if (corrupt) File.WriteAllText(fixture.Locations.DatabasePath, "genuinely corrupt SQLite database");
        File.WriteAllText(Path.Combine(fixture.Locations.CatalogRoot, "catalog.db-journal"), "rollback bytes");
        var original = RestoreTestSupport.Generation(fixture.Locations.CatalogRoot);
        var expected = RestoreTestSupport.Payload(fixture.Backup);
        var journal = new CatalogLocationMigrator(fixture.Service);
        await new CatalogRestoreExecutor(journal).StageAsync(fixture.Locations, fixture.Backup);
        Assert.Equal(original, RestoreTestSupport.Generation(fixture.Locations.CatalogRoot));
        await journal.ExecutePendingAsync();
        Assert.Equal(expected, RestoreTestSupport.Generation(fixture.Locations.CatalogRoot));
        RestoreTestSupport.AssertPreserved(fixture, original, corrupt);
        Assert.Empty(Directory.GetFiles(fixture.Locations.AssetsRoot, "*", SearchOption.AllDirectories));
        Assert.False(File.Exists(journal.JournalPath));
        Assert.Contains(fixture.Backup, CatalogRestoreExecutor.ReadNotice(fixture.Locations.CatalogRoot));
        await journal.ExecutePendingAsync();
        Assert.NotNull(CatalogRestoreExecutor.ReadNotice(fixture.Locations.CatalogRoot));
        CatalogRestoreExecutor.AcknowledgeNotice(fixture.Locations.CatalogRoot);
        Assert.Null(CatalogRestoreExecutor.ReadNotice(fixture.Locations.CatalogRoot));
        using var catalog = new CatalogService();
        await catalog.InitializeAsync(fixture.Locations);
    }

    [Theory]
    [InlineData("newer")]
    [InlineData("damaged")]
    [InlineData("payload")]
    [InlineData("identity")]
    public async Task Refusals_DoNotChangeDisk(string failure)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        if (failure == "newer") RestoreTestSupport.ChangeManifest(f.Backup,
            m => m with { SchemaVersion = CatalogMigrations.CurrentVersion + 1 });
        if (failure == "damaged") RestoreTestSupport.ChangeManifest(f.Backup, m => m with { Damaged = true });
        if (failure == "payload")
        {
            using var zip = ZipFile.Open(f.Backup, ZipArchiveMode.Update);
            using var stream = zip.GetEntry("catalog.db")!.Open();
            stream.WriteByte(0);
        }
        if (failure == "identity")
            File.WriteAllText(Path.Combine(f.Locations.CatalogRoot, ".catalog-identity"),
                $$"""{"version":1,"catalogId":"{{Guid.NewGuid()}}"}""");
        var before = Disk();
        var journal = new CatalogLocationMigrator(f.Service);
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup));
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        Assert.Equal(before, Disk());
        SortedDictionary<string, string> Disk() => new(Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, CatalogBackupService.Hash));
    }

    [Fact]
    public async Task StagedArchiveChanged_RefusesBeforeAnyCatalogChange()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup);
        RestoreTestSupport.ChangeManifest(f.Backup, m => m with { Utc = m.Utc.AddDays(1) });
        var before = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        await Assert.ThrowsAsync<IOException>(journal.ExecutePendingAsync);
        Assert.Equal(before, RestoreTestSupport.Generation(f.Locations.CatalogRoot));
        Assert.False(File.Exists(journal.JournalPath));
    }

    [Fact]
    public async Task Listing_OnlyReadsMetadata_AndUncheckedArchivesRestore()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        File.Delete(Path.ChangeExtension(f.Backup, ".manifest.json"));
        using var catalog = new CatalogService(f.Locations.CatalogRoot);
        var service = new CatalogBackupService(catalog);
        service.Step = _ => Assert.Fail("Listing opened an archive");
        Assert.Equal("not checked", Assert.Single(service.ListForRestore()).State);
        service.Attributes = _ => FileAttributes.Offline;
        Assert.StartsWith("cloud-only", Assert.Single(service.ListForRestore()).State);
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup);
        Assert.True(File.Exists(Path.ChangeExtension(f.Backup, ".manifest.json")));
        await journal.ExecutePendingAsync();
        Assert.Equal(RestoreTestSupport.Payload(f.Backup), RestoreTestSupport.Generation(f.Locations.CatalogRoot));
    }

    [Fact]
    public async Task MoveAndRestore_AreMutuallyExclusive()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var journal = new CatalogLocationMigrator(f.Service);
        await journal.StageMoveAsync(f.Locations, CatalogLocationMoveKind.SetAside);
        var before = File.ReadAllText(journal.JournalPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup));
        Assert.Equal(before, File.ReadAllText(journal.JournalPath));
        journal.DeleteJournal();
        await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup);
        before = File.ReadAllText(journal.JournalPath);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            journal.StageMoveAsync(f.Locations, CatalogLocationMoveKind.SetAside));
        Assert.Equal(before, File.ReadAllText(journal.JournalPath));
    }

    [Fact]
    public async Task FailedFullCheck_MarksTheChooserRowDamaged()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        using (var zip = ZipFile.Open(f.Backup, ZipArchiveMode.Update))
        using (var stream = zip.GetEntry("catalog.db")!.Open()) stream.WriteByte(0);
        using var catalog = new CatalogService(f.Locations.CatalogRoot);
        var service = new CatalogBackupService(catalog);
        Assert.Equal("verified when created", Assert.Single(service.ListForRestore()).State);
        Assert.Throws<InvalidDataException>(() => service.CheckForRestore(f.Backup));
        var row = Assert.Single(service.ListForRestore());
        Assert.Equal("damaged", row.State);
        Assert.False(row.CanRestore);
        Assert.Equal("damaged", Assert.Single(new CatalogBackupService(catalog).ListForRestore()).State);
    }

    [Fact]
    public async Task Restore_WhenStartingPresetsAreMissing_RecreatesTheCompletePayload()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        Directory.Delete(f.Locations.PresetsRoot, recursive: true);
        var before = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        var store = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(store).StageAsync(f.Locations, f.Backup);
        await store.ExecutePendingAsync();
        Assert.Equal(RestoreTestSupport.Payload(f.Backup), RestoreTestSupport.Generation(f.Locations.CatalogRoot));
        RestoreTestSupport.AssertPreserved(f, before, damaged: false);
    }

    [Theory]
    [InlineData("newer")]
    [InlineData("damaged")]
    [InlineData("payload")]
    public async Task Execution_RechecksRefusals_WithoutChangingDisk(string reason)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var store = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(store).StageAsync(f.Locations, f.Backup);
        if (reason == "newer") RestoreTestSupport.ChangeManifest(f.Backup,
            m => m with { SchemaVersion = CatalogMigrations.CurrentVersion + 1 });
        else if (reason == "damaged") RestoreTestSupport.ChangeManifest(f.Backup, m => m with { Damaged = true });
        else
        {
            using var zip = ZipFile.Open(f.Backup, ZipArchiveMode.Update);
            using var stream = zip.GetEntry("catalog.db")!.Open();
            stream.WriteByte(0);
        }
        var before = Disk();
        var exception = await Assert.ThrowsAnyAsync<Exception>(store.ExecutePendingAsync);
        if (reason == "newer") Assert.Contains("schema", exception.Message);
        before.Remove(store.JournalPath);
        Assert.False(File.Exists(store.JournalPath));
        Assert.Equal(before, Disk());
        SortedDictionary<string, string> Disk() => new(Directory.GetFiles(directory.Path, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, CatalogBackupService.Hash));
    }

    [Fact]
    public async Task MetadataListing_DoesNotOpenAnExclusivelyLockedArchiveOrCloudSidecar()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        using var catalog = new CatalogService(f.Locations.CatalogRoot);
        var service = new CatalogBackupService(catalog);
        using var archiveLock = new FileStream(f.Backup, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Equal("verified when created", Assert.Single(service.ListForRestore()).State);
        using var sidecarLock = new FileStream(Path.ChangeExtension(f.Backup, ".manifest.json"),
            FileMode.Open, FileAccess.Read, FileShare.None);
        service.Attributes = _ => FileAttributes.Offline;
        Assert.StartsWith("cloud-only", Assert.Single(service.ListForRestore()).State);
    }
}

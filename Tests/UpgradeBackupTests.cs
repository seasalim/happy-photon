using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(BackupBaselineCollection.Name)]
public sealed class UpgradeBackupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingCatalog_PackagesBeforeMigration_ListsAndRestoresOldSchema(bool imagesOnly)
    {
        using var directory = new TemporaryDirectory();
        var locations = RestoreTestSupport.Service(directory.Path);
        var resolved = await locations.CreateFreshAsync(useStandardCatalog: true);
        await UpgradeTestSupport.SeedAsync(resolved.CatalogRoot, imagesOnly);
        var original = RestoreTestSupport.Generation(resolved.CatalogRoot);
        string backupPath;

        using (var catalog = new CatalogService())
        {
            await catalog.InitializeAsync(resolved);
            Assert.Equal("4", await catalog.GetAppSettingAsync("schema_version"));
            var backup = new CatalogBackupService(catalog);
            var row = Assert.Single(backup.ListForRestore());
            Assert.True(row.CanRestore);
            backupPath = row.Path;
            Assert.Contains("before-upgrade", row.Description);
            using var checkedBackup = backup.CheckForRestore(row.Path);
            Assert.Equal(imagesOnly ? 0 : 3, checkedBackup.Manifest.SchemaVersion);
            Assert.Equal(original, RestoreTestSupport.Payload(row.Path));
        }

        var store = new CatalogLocationMigrator(locations);
        await new CatalogRestoreExecutor(store).StageAsync(resolved, backupPath);
        await store.ExecutePendingAsync();
        Assert.Equal(original, RestoreTestSupport.Generation(resolved.CatalogRoot));
        using var old = CatalogService.OpenBackupCopy(resolved.DatabasePath, SqliteOpenMode.ReadOnly);
        Assert.Equal((1L, imagesOnly ? 0L : 3L), CatalogService.ReadBackupFacts(old));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disk-full")]
    [InlineData("verification")]
    public async Task Failure_PreservesBytes_RetryProtectsBeforeMigration(string cause)
    {
        using var directory = new TemporaryDirectory();
        await UpgradeTestSupport.SeedAsync(directory.Path);
        var original = RestoreTestSupport.Generation(directory.Path);
        using var catalog = new CatalogService(directory.Path)
        {
            ConfigureUpgradeBackup = backup => UpgradeTestSupport.Fail(backup, cause)
        };

        await Assert.ThrowsAsync<CatalogUpgradeBackupException>(catalog.InitializeAsync);
        Assert.Null(catalog.OpenCatalogIdentity);
        Assert.Equal(original, RestoreTestSupport.Generation(directory.Path));
        catalog.ConfigureUpgradeBackup = null;
        await catalog.InitializeAsync();
        Assert.Equal("4", await catalog.GetAppSettingAsync("schema_version"));
        Assert.Single(new CatalogBackupService(catalog).ListForRestore());
    }

    [Fact]
    public async Task FailedWrittenBackup_RemovesPartials_AndRetryPublishesOnce()
    {
        using var directory = new TemporaryDirectory();
        await UpgradeTestSupport.SeedAsync(directory.Path);
        var original = RestoreTestSupport.Generation(directory.Path);
        using var catalog = new CatalogService(directory.Path);
        var backup = new CatalogBackupService(catalog);
        var reached = false;
        catalog.ConfigureUpgradeBackup = attempt => attempt.Step = step =>
        {
            if (step != "after:preserve-sidecar") return;

            Assert.True(new FileInfo(Assert.Single(Directory.GetFiles(backup.Folder, "*.partial.zip"))).Length > 0);
            Assert.Single(Directory.GetFiles(backup.Folder, "*.partial.json"));
            reached = true;
            throw new IOException("The backup drive is full.");
        };

        await Assert.ThrowsAsync<CatalogUpgradeBackupException>(catalog.InitializeAsync);
        Assert.True(reached);
        Assert.Equal(original, RestoreTestSupport.Generation(directory.Path));
        Assert.Empty(Directory.GetFiles(backup.Folder, "*.partial.*"));
        catalog.ConfigureUpgradeBackup = null;
        await catalog.InitializeAsync();
        using var verified = backup.CheckForRestore(Assert.Single(backup.ListForRestore()).Path);
        Assert.Equal("before-upgrade", verified.Manifest.Kind);
        Assert.Equal(3, verified.Manifest.SchemaVersion);
        Assert.Equal(2, Directory.GetFiles(backup.Folder).Length);
    }

    [Fact]
    public async Task BeforeUpgrade_SweepsOwnedOrphansBeforeWriting_LeavesOtherFiles()
    {
        using var directory = new TemporaryDirectory();
        await UpgradeTestSupport.SeedAsync(directory.Path);
        using var catalog = new CatalogService(directory.Path);
        var backup = new CatalogBackupService(catalog);
        Directory.CreateDirectory(backup.Folder);
        var stem = Path.Combine(backup.Folder, $"hp-backup-{Guid.NewGuid():N}");
        var orphans = new[] { stem + ".partial.zip", stem + ".partial.json", stem + ".partial.db" };

        foreach (var path in orphans) File.WriteAllText(path, "orphan");

        var foreign = Path.Combine(backup.Folder, "hp-backup-foreign.partial.zip");
        File.WriteAllText(foreign, "keep");
        catalog.ConfigureUpgradeBackup = attempt => attempt.Step = step =>
        {
            if (step != "before:preserve-copy:catalog.db") return;

            Assert.All(orphans, path => Assert.False(File.Exists(path)));
        };

        await catalog.InitializeAsync();
        Assert.Equal("keep", File.ReadAllText(foreign));
        Assert.Single(backup.ListForRestore());
    }

    [Fact]
    public async Task BeforeUpgrade_IsNotPrunedByManualRetention_OrRepeatedOnNormalOpen()
    {
        using var directory = new TemporaryDirectory();
        await UpgradeTestSupport.SeedAsync(directory.Path);
        string preserved;

        using (var catalog = new CatalogService(directory.Path))
        {
            await catalog.InitializeAsync();
            var backup = new CatalogBackupService(catalog);
            preserved = Assert.Single(backup.ListForRestore()).Path;

            for (var i = 0; i < 6; i++)
                await backup.BackupAsync();

            Assert.Equal(6, backup.ListForRestore().Count);
            Assert.True(File.Exists(preserved));
        }

        using var reopened = new CatalogService(directory.Path);
        await reopened.InitializeAsync();
        var listed = new CatalogBackupService(reopened).ListForRestore();
        Assert.Equal(6, listed.Count);
        Assert.Single(listed, row => row.Description.Contains("before-upgrade"));
    }

    [Fact]
    public async Task FreshAndCurrentCatalogs_NeverAcquireBackupPath()
    {
        using var directory = new TemporaryDirectory();
        var before = CatalogBackupService.FolderAccessCount;

        for (var launch = 0; launch < 2; launch++)
        {
            using var catalog = new CatalogService(directory.Path);
            await catalog.InitializeAsync();
        }

        Assert.Equal(before, CatalogBackupService.FolderAccessCount);
    }
}

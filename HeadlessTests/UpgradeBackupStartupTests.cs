using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class UpgradeBackupStartupTests(BackupCatalogFixtures fixtures)
{
    [AvaloniaTheory]
    [InlineData("missing", false)]
    [InlineData("disk-full", false)]
    [InlineData("verification", false)]
    [InlineData("missing", true)]
    [InlineData("disk-full", true)]
    [InlineData("verification", true)]
    public async Task FailedBackup_ShowsD6_WithoutChangingDatabaseOrJournal(string cause, bool hot)
    {
        using var directory = new TemporaryDirectory();
        var service = RestoreTestSupport.Service(directory.Path);
        var locations = await service.CreateFreshAsync(useStandardCatalog: true);
        await UpgradeTestSupport.SeedAsync(locations.CatalogRoot);
        File.Copy((await fixtures.Schema3Archive).DatabasePath, locations.DatabasePath, overwrite: true);

        if (hot)
        {
            await RestoreHotJournalStartupTests.KillWriter(locations.DatabasePath);
            Assert.False(AppDataLocationService.HasCatalogSignature(locations.CatalogRoot));
        }

        var original = RestoreTestSupport.Generation(locations.CatalogRoot);
        using var catalog = new CatalogService
        {
            ConfigureUpgradeBackup = backup => UpgradeTestSupport.Fail(backup, cause)
        };
        await using var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        await window.InitializeApplicationAsync(vm, catalog, service, new CatalogLocationMigrator(service), null)
            .WaitAsync(TestWaits.Condition);
        Assert.True(vm.IsStartupError, vm.FirstRunErrorMessage);
        Assert.True(vm.IsUpgradeBackupFailure);
        Assert.True(vm.RetryStartupCommand.CanExecute(null));
        Assert.True(vm.UpgradeWithoutBackupCommand.CanExecute(null));
        Assert.Null(catalog.OpenCatalogIdentity);
        Assert.Equal(original, RestoreTestSupport.Generation(locations.CatalogRoot));
        await vm.RetryStartupCommand.ExecuteAsync(null);
        Assert.True(vm.IsUpgradeBackupFailure);
        Assert.Equal(original, RestoreTestSupport.Generation(locations.CatalogRoot));
    }

    [AvaloniaFact]
    public async Task ExplicitOverride_MigratesAndIsNotPersisted()
    {
        using var directory = new TemporaryDirectory();
        var service = RestoreTestSupport.Service(directory.Path);
        var locations = await service.CreateFreshAsync(useStandardCatalog: true);
        await UpgradeTestSupport.SeedAsync(locations.CatalogRoot);
        using var catalog = new CatalogService
        {
            ConfigureUpgradeBackup = backup => UpgradeTestSupport.Fail(backup, "disk-full")
        };
        await using var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        await window.InitializeApplicationAsync(vm, catalog, service, new CatalogLocationMigrator(service), null)
            .WaitAsync(TestWaits.Condition);
        Assert.True(vm.IsUpgradeBackupFailure);
        await vm.UpgradeWithoutBackupCommand.ExecuteAsync(null);
        Assert.Equal(StartupGateState.Ready, vm.StartupGateState);
        Assert.Equal("4", await catalog.GetAppSettingAsync("schema_version"));
        Assert.Empty(new CatalogBackupService(catalog).ListForRestore());
        scope.Dispose();
        catalog.Dispose();

        await UpgradeTestSupport.SeedAsync(locations.CatalogRoot);
        using var next = new CatalogService(locations.CatalogRoot)
        {
            ConfigureUpgradeBackup = backup => UpgradeTestSupport.Fail(backup, "disk-full")
        };
        Assert.False(next.UpgradeWithoutBackup);
        await Assert.ThrowsAsync<CatalogUpgradeBackupException>(next.InitializeAsync);
    }

    [AvaloniaFact]
    public async Task HotJournal_BackupRestoresOriginalBytesAndOpensAtOldSchema()
    {
        using var directory = new TemporaryDirectory();
        var service = RestoreTestSupport.Service(directory.Path);
        var locations = await service.CreateFreshAsync(useStandardCatalog: true);
        await UpgradeTestSupport.SeedAsync(locations.CatalogRoot);
        await RestoreHotJournalStartupTests.KillWriter(locations.DatabasePath);
        var original = RestoreTestSupport.Generation(locations.CatalogRoot);
        string path;

        using (var catalog = new CatalogService())
        {
            await catalog.InitializeAsync(locations);
            var backup = new CatalogBackupService(catalog);
            path = Assert.Single(backup.ListForRestore()).Path;
            Assert.Equal(original, RestoreTestSupport.Payload(path));
            Assert.Equal("4", await catalog.GetAppSettingAsync("schema_version"));
        }

        var store = new CatalogLocationMigrator(service);
        await new CatalogRestoreExecutor(store).StageAsync(locations, path);
        await store.ExecutePendingAsync();
        Assert.Equal(original, RestoreTestSupport.Generation(locations.CatalogRoot));
        using var old = CatalogService.OpenBackupCopy(locations.DatabasePath, SqliteOpenMode.ReadWrite);
        Assert.Equal((1L, 3L), CatalogService.ReadBackupFacts(old));
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImagesOnlyIsExisting_TablelessIsFresh(bool imagesOnly)
    {
        using var directory = new TemporaryDirectory();
        var service = RestoreTestSupport.Service(directory.Path);
        var locations = await service.CreateFreshAsync(useStandardCatalog: true);

        if (imagesOnly)
        {
            await UpgradeTestSupport.SeedAsync(locations.CatalogRoot, imagesOnly: true);
        }
        else
        {
            using var empty = CatalogService.OpenBackupCopy(locations.DatabasePath, SqliteOpenMode.ReadWriteCreate);
        }

        using var catalog = new CatalogService();
        await using var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        await window.InitializeApplicationAsync(vm, catalog, service, new CatalogLocationMigrator(service), null)
            .WaitAsync(TestWaits.Condition);
        Assert.False(vm.IsStartupError, vm.FirstRunErrorMessage);

        if (imagesOnly)
        {
            Assert.Equal("4", await catalog.GetAppSettingAsync("schema_version"));
            Assert.Single(new CatalogBackupService(catalog).ListForRestore());
        }
        else
        {
            Assert.Null(catalog.OpenCatalogIdentity);
            Assert.True(vm.IsFirstRunVisible);
        }
    }

    [AvaloniaFact]
    public async Task StartupUpgradeBackupFailed_RendersShowcase()
    {
        using var catalog = new CatalogService();
        await using var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        vm.ShowUpgradeBackupFailure("The before-upgrade backup failed: the backup drive is full. The catalog has not been upgraded.");
        ShowcaseTestHelper.Capture("startup-upgrade-backup-failed", scope, new PixelSize(1200, 700), ThemeVariant.Dark,
            shown =>
            {
                var buttons = shown.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.IsEffectivelyVisible).Select(button => button.Content).ToArray();
                Assert.Contains("Retry", buttons);
                Assert.Contains("Upgrade without a backup", buttons);
                Assert.Contains("Close", buttons);
                Assert.DoesNotContain("Restore from backup…", buttons);
            });
    }
}

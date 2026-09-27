using Avalonia.Headless.XUnit;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RestoreStartupTests
{
    [AvaloniaTheory]
    [InlineData("deleted")]
    [InlineData("changed")]
    [InlineData("identity")]
    public async Task PreparedRefusal_ClearsJournal_AllowsRetryAndAnotherRestore(string reason)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var archiveBytes = File.ReadAllBytes(f.Backup);
        var journal = new CatalogLocationMigrator(f.Service);
        var executor = new CatalogRestoreExecutor(journal);
        await executor.StageAsync(f.Locations, f.Backup);
        if (reason == "deleted") File.Delete(f.Backup);
        else if (reason == "changed") RestoreTestSupport.ChangeManifest(f.Backup, m => m with { Utc = m.Utc.AddDays(1) });
        else File.WriteAllText(Path.Combine(f.Locations.CatalogRoot, ".catalog-identity"),
            $$"""{"version":1,"catalogId":"{{Guid.NewGuid()}}"}""");
        var expected = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        try
        {
            await window.InitializeApplicationAsync(vm, catalog, f.Service, journal, Path.Combine(directory.Path, "pictures"));
            Assert.True(vm.IsStartupError);
            Assert.Contains(reason == "deleted" ? "find" : reason == "changed" ? "changed" : "different catalog",
                vm.FirstRunErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(journal.JournalPath));
            Assert.Equal(expected, RestoreTestSupport.Generation(f.Locations.CatalogRoot));
            await vm.RetryStartupAsync!();
            Assert.Equal(StartupGateState.Ready, vm.StartupGateState);
            Assert.True(catalog.OpenCatalogIdentity.HasValue, vm.FirstRunErrorMessage);
            AppDataRootOwnership.AssertAppOwned(f.Locations.CacheRoot);
            File.WriteAllBytes(f.Backup, archiveBytes);
            await executor.StageAsync(f.Locations, f.Backup, acknowledgeDifferentCatalog: true);
            Assert.True(File.Exists(journal.JournalPath));
        }
        finally { scope.Dispose(); await vm.DisposeAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task ErrorScreen_RestoresAndBindsFreshServices(bool corrupt, bool reuseChooserCheck, bool deleteCache = false)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        if (corrupt) File.WriteAllText(f.Locations.DatabasePath, "not SQLite");
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var opens = 0;
        var journal = new CatalogLocationMigrator(f.Service)
        {
            RestoreStep = step => { if (step == "archive-open") opens++; }
        };
        var notices = 0;
        window.ShowRestoreNotice = message => { notices++; Assert.Contains(f.Backup, message); return Task.CompletedTask; };
        window.BeforeStartupSettingsLoad = () => throw new IOException("settings load failed");
        try
        {
            await window.InitializeApplicationAsync(vm, catalog, f.Service, journal, Path.Combine(directory.Path, "pictures"));
            Assert.True(vm.IsStartupError);
            if (!corrupt) Assert.NotNull(catalog.OpenCatalogIdentity);
            window.BeforeStartupSettingsLoad = null;
            CheckedCatalogBackup? check = null;
            if (reuseChooserCheck)
            {
                var chooser = new RestoreBackupViewModel(f.Locations.CatalogRoot)
                {
                    ChooseFileAsync = () => Task.FromResult<string?>(f.Backup),
                    ConfirmAsync = _ => Task.FromResult(true),
                    Accepted = (_, _, accepted) => check = accepted
                };
                await chooser.ChooseFileCommand.ExecuteAsync(null);
                Assert.NotNull(check);
            }
            if (deleteCache) Directory.Delete(f.Locations.CacheRoot, recursive: true);
            await window.RestoreCatalogAsync(vm, f.Backup, false, check).WaitAsync(TestWaits.Condition);
            Assert.Equal(reuseChooserCheck ? 1 : 2, opens);
            var fresh = Assert.IsType<MainWindowViewModel>(window.DataContext);
            Assert.NotSame(vm, fresh);
            Assert.True(fresh.StartupGateState == StartupGateState.Ready, fresh.FirstRunErrorMessage);
            AppDataRootOwnership.AssertAppOwned(f.Locations.CacheRoot);
            Assert.False(File.Exists(journal.JournalPath));
            Assert.Single(fresh.PresetService.UserPresets);
            Assert.Equal("First", fresh.PresetService.UserPresets[0].Name);
            Assert.Null(catalog.OpenCatalogIdentity);
            Assert.Equal(1, notices);
            Assert.Null(CatalogRestoreExecutor.ReadNotice(f.Locations.CatalogRoot));
            await fresh.RetryStartupAsync!();
            Assert.Equal(1, notices);
        }
        finally
        {
            var active = Assert.IsType<MainWindowViewModel>(window.DataContext);
            scope.Dispose();
            await active.DisposeAsync();
            var field = typeof(MainWindowViewModel).GetField("_catalogService",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            ((CatalogService)field.GetValue(active)!).Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StagedRestore_RunsBeforeOpen_NoticeSurvivesStartupFailure(bool deleteCache)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup);
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        if (deleteCache) Directory.Delete(f.Locations.CacheRoot, recursive: true);
        var noticeCount = 0;
        window.ShowRestoreNotice = _ => { noticeCount++; return Task.CompletedTask; };
        window.BeforeStartupSettingsLoad = () => throw new IOException("settings");
        try
        {
            await window.InitializeApplicationAsync(vm, catalog, f.Service, journal, Path.Combine(directory.Path, "pictures"));
            Assert.True(vm.IsStartupError);
            Assert.True(catalog.OpenCatalogIdentity.HasValue, vm.FirstRunErrorMessage);
            AppDataRootOwnership.AssertAppOwned(f.Locations.CacheRoot);
            Assert.False(File.Exists(journal.JournalPath));
            Assert.NotNull(CatalogRestoreExecutor.ReadNotice(f.Locations.CatalogRoot));
            Assert.Equal(0, noticeCount);
            window.BeforeStartupSettingsLoad = null;
            await vm.RetryStartupAsync!();
            Assert.Equal(StartupGateState.Ready, vm.StartupGateState);
            Assert.Equal(1, noticeCount);
            Assert.Null(CatalogRestoreExecutor.ReadNotice(f.Locations.CatalogRoot));
        }
        finally { scope.Dispose(); await vm.DisposeAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Chooser_UncheckedArchive_ChecksBeforeConfirming(bool chooseFile)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        File.Delete(Path.ChangeExtension(f.Backup, ".manifest.json"));
        var vm = new RestoreBackupViewModel(f.Locations.CatalogRoot);
        vm.Selected = Assert.Single(vm.Rows);
        Assert.Equal("not checked", vm.Selected.State);
        var confirmed = false;
        var accepted = false;
        vm.ChooseFileAsync = () => Task.FromResult<string?>(f.Backup);
        vm.ConfirmAsync = _ => { confirmed = true; return Task.FromResult(true); };
        CheckedCatalogBackup? checkedBackup = null;
        vm.Accepted = (path, different, check) =>
        {
            accepted = path == f.Backup && !different;
            checkedBackup = check;
        };
        await (chooseFile ? vm.ChooseFileCommand : vm.RestoreCommand).ExecuteAsync(null);
        Assert.True(confirmed);
        Assert.True(accepted);
        var opens = 0;
        void Count(string step) { if (step == "archive-open") opens++; }
        var journal = new CatalogLocationMigrator(f.Service) { RestoreStep = Count };
        await new CatalogRestoreExecutor(journal) { Step = Count }
            .StageAsync(f.Locations, f.Backup, Assert.IsType<CheckedCatalogBackup>(checkedBackup), false);
        Assert.Equal(0, opens); // The dialog's full check is reused at staging.
        await journal.ExecutePendingAsync();
        Assert.Equal(1, opens); // Execution still independently checks the archive.
        Assert.Equal(RestoreTestSupport.Payload(f.Backup), RestoreTestSupport.Generation(f.Locations.CatalogRoot));
        Assert.True(File.Exists(Path.ChangeExtension(f.Backup, ".manifest.json")));
    }
}

using Avalonia.Headless.XUnit;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class StorageRestoreQuitTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldestStagedBackup_SurvivesDueQuitBackupAndRestoresAtLaunch(bool closeDuringStaging)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        using (var setup = new CatalogService(f.Locations.CatalogRoot))
        {
            await setup.InitializeAsync();
            var backup = new CatalogBackupService(setup);
            RestoreTestSupport.ChangeManifest(f.Backup, manifest => manifest with { Utc = DateTimeOffset.UtcNow.AddDays(-20) });
            for (var i = 0; i < 4; i++)
            {
                backup.UtcNow = () => DateTimeOffset.UtcNow.AddDays(-15 + i);
                await backup.BackupAsync();
            }
            Assert.Equal(5, backup.List().Count);
            backup.UtcNow = () => DateTimeOffset.UtcNow;
            Assert.True(backup.IsDue());
        }
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow(); // test-teardown-policy: allow - scope owns binding, finally awaits real close.
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        var journal = new CatalogLocationMigrator(f.Service);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var decidingBackup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        journal.RestoreStep = step =>
        {
            if (step == "before:quit-backup") decidingBackup.TrySetResult();
            if (!closeDuringStaging || step != "before:commit:Prepared") return;
            entered.TrySetResult();
            Assert.True(release.Wait(TestWaits.Condition));
        };
        Task staging = Task.CompletedTask;
        string[] before;
        var quitBackups = 0;
        try
        {
            await window.InitializeApplicationAsync(vm, catalog, f.Service, journal, Path.Combine(directory.Path, "pictures"));
            await vm.BackupNoticeLoad;
            var storage = vm.StorageSettings!;
            var chooser = new RestoreBackupViewModel(f.Locations.CatalogRoot, staged: true)
            {
                ConfirmAsync = _ => Task.FromResult(true)
            };
            storage.RequestRestoreAsync = () => StorageRestoreTests.AcceptAsync(chooser, f.Backup, false);

            var backup = new CatalogBackupService(catalog);
            before = Directory.GetFiles(backup.Folder).Order().Select(CatalogBackupService.Hash).ToArray();
            window.BackupOnQuitAsync = async () => { quitBackups++; await backup.BackupIfDueAsync(); };
            staging = storage.RestoreBackupCommand.ExecuteAsync(null);
            if (closeDuringStaging)
            {
                await entered.Task.WaitAsync(TestWaits.Condition);
                Assert.False(File.Exists(journal.JournalPath));
                window.Close();
                await decidingBackup.Task.WaitAsync(TestWaits.Condition);
                Assert.Equal(0, quitBackups);
            }
            else await staging.WaitAsync(TestWaits.Condition);
        }
        finally
        {
            release.Set();
            await staging.WaitAsync(TestWaits.Condition);
            window.Close();
            await closed.Task.WaitAsync(TestWaits.Condition);
        }
        Assert.Null(vm.StorageSettings!.CatalogError);
        Assert.Equal(0, quitBackups);
        Assert.True(File.Exists(f.Backup));
        Assert.Equal(before, Directory.GetFiles(new CatalogBackupService(catalog).Folder).Order()
            .Select(CatalogBackupService.Hash).ToArray());
        catalog.Dispose();
        using var restored = new CatalogService();
        await using var fresh = new MainWindowViewModel(restored);
        var next = new MainWindow();
        using var nextScope = TestUiScope.ForMainWindow(next, fresh);
        var notices = 0;
        next.ShowRestoreNotice = _ => { notices++; return Task.CompletedTask; };
        await next.InitializeApplicationAsync(fresh, restored, f.Service, journal, Path.Combine(directory.Path, "pictures"));
        await fresh.BackupNoticeLoad;
        Assert.Equal(StartupGateState.Ready, fresh.StartupGateState);
        Assert.Equal(1, notices);
        Assert.Null(fresh.StatusMessage);
        Assert.Single(fresh.PresetService.UserPresets);
        Assert.False(File.Exists(journal.JournalPath));
        Assert.Null(CatalogRestoreExecutor.ReadNotice(f.Locations.CatalogRoot));
        await fresh.PresentRestoreNoticeAsync!();
        Assert.Equal(1, notices);
    }

    [AvaloniaFact]
    public async Task RefusedStagingEarlierInSession_StillAllowsTheQuitBackup()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow(); // test-teardown-policy: allow - scope owns binding, the test awaits real close.
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        var journal = new CatalogLocationMigrator(f.Service);
        var quitBackups = 0;

        await window.InitializeApplicationAsync(vm, catalog, f.Service, journal, Path.Combine(directory.Path, "pictures"));
        window.BackupOnQuitAsync = () => { quitBackups++; return Task.CompletedTask; };
        var refused = journal.TrackStaging(() => throw new InvalidOperationException("Refused staging."));
        await Assert.ThrowsAsync<InvalidOperationException>(() => refused);

        window.Close();
        await closed.Task.WaitAsync(TestWaits.Condition);

        Assert.Equal(1, quitBackups);
        Assert.False(File.Exists(journal.JournalPath));
        catalog.Dispose();
    }
}

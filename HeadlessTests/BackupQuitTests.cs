using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BackupQuitTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteFirstRunQuit_DoesNotBackUpOrRecordOutcome(bool lifetimeQuit)
    {
        using var directory = new TemporaryDirectory();
        var pictures = Directory.CreateDirectory(Path.Combine(directory.Path, "pictures")).FullName;
        var locations = new AppDataLocationService(new AppDataPlatformPaths(pictures,
            Path.Combine(directory.Path, "pointer"), Path.Combine(directory.Path, "data"),
            Path.Combine(directory.Path, "cache")), _ => null);
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            await window.InitializeApplicationAsync(vm, catalog, locations,
                new CatalogLocationMigrator(locations), pictures);
            await vm.ContinueFirstRunCommand.ExecuteAsync(null);
            await vm.ContinueFirstRunCommand.ExecuteAsync(null);
            Assert.Equal(FirstRunStep.Pictures, vm.FirstRunStep);
            Assert.NotNull(catalog.OpenCatalogIdentity);
            Assert.False(vm.CanPersistFolderSession);
            Assert.Null((await new AppSettingsService(catalog).LoadAsync()).FirstRunExperienceVersion);
            if (lifetimeQuit)
            {
                var quit = new ShutdownRequestedEventArgs();
                window.OnShutdownRequested(null, quit);
                Assert.True(quit.Cancel);
            }
            else window.Close();
        }
        finally
        {
            window.Close();
            await closed.Task.WaitAsync(TestWaits.Condition);
        }
        Assert.False(Directory.Exists(Path.Combine(catalog.CatalogPath, "Backups")));
        Assert.Null(await catalog.GetAppSettingAsync(CatalogBackupService.OutcomeKey));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowAndLifetimeQuit_WaitForOneBackup(bool lifetimeQuit)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var vm = new MainWindowViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var backup = new CatalogBackupService(catalog);
        var window = new MainWindow(); // test-teardown-policy: allow - ForMainWindow owns theme/binding; finally awaits real close.
        using var scope = TestUiScope.ForMainWindow(window, vm);
        window.Closed += (_, _) => closed.TrySetResult();
        window.BackupOnQuitAsync = async () =>
        {
            calls++;
            Assert.Null(window.DataContext);
            Assert.False(window.IsVisible);
            entered.TrySetResult();
            await release.Task;
            await backup.BackupIfDueAsync();
        };
        try
        {
            if (lifetimeQuit)
            {
                var quit = new ShutdownRequestedEventArgs();
                window.OnShutdownRequested(null, quit);
                Assert.True(quit.Cancel);
            }
            else window.Close();
            await entered.Task.WaitAsync(TestWaits.Condition);
            window.Close();
            var again = new ShutdownRequestedEventArgs();
            window.OnShutdownRequested(null, again);
            Assert.True(again.Cancel);
            Assert.False(closed.Task.IsCompleted);
            Assert.Equal(1, calls);
        }
        finally
        {
            release.TrySetResult();
            window.Close();
            await closed.Task.WaitAsync(TestWaits.Condition);
        }
        Assert.Single(Directory.GetFiles(backup.Folder, "*.manifest.json"));
        Assert.Equal("ok", System.Text.Json.JsonSerializer.Deserialize<BackupOutcome>(
            (await catalog.GetAppSettingAsync(CatalogBackupService.OutcomeKey))!)!.Status);
    }
}

using Avalonia.Headless.XUnit;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class StorageStagingRaceTests
{
    [AvaloniaFact]
    public async Task MoveCommittedDuringRestoreStaging_IsNotReplaced()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var journal = new CatalogLocationMigrator(f.Service)
        {
            RestoreStep = step =>
            {
                if (step != "before:commit:Prepared") return;
                entered.TrySetResult();
                Assert.True(release.Wait(TestWaits.Condition));
            }
        };
        var storage = new StorageSettingsViewModel(f.Locations, journal);
        var chooser = new RestoreBackupViewModel(f.Locations.CatalogRoot, staged: true)
        {
            ConfirmAsync = _ => Task.FromResult(true)
        };
        storage.RequestRestoreAsync = () => StorageRestoreTests.AcceptAsync(chooser, f.Backup, false);
        var staging = storage.RestoreBackupCommand.ExecuteAsync(null);
        var destination = Path.Combine(directory.Path, "destination");
        AppDataRootOwnership.ClaimFresh(destination);
        string? committed = null;
        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            await journal.StageMoveAsync(f.Locations, CatalogLocationMoveKind.Catalog, destination);
            committed = await File.ReadAllTextAsync(journal.JournalPath);
        }
        finally
        {
            release.Set();
            await staging.WaitAsync(TestWaits.Condition);
        }
        Assert.Equal("A storage move or restore is already pending.", storage.CatalogError);
        Assert.Equal(committed, await File.ReadAllTextAsync(journal.JournalPath));
        Assert.Equal(CatalogLocationMoveKind.Catalog, (await journal.ReadJournalAsync()).Kind);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(journal.JournalPath)!, "*.tmp"));
    }

    [AvaloniaFact]
    public async Task RestoreInFlight_DisablesStorageCommands_AndCancellationReenablesThem()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var storage = new StorageSettingsViewModel(f.Locations, new CatalogLocationMigrator(f.Service))
        {
            PendingCatalogRoot = Path.Combine(directory.Path, "catalog-destination"),
            PendingCacheRoot = Path.Combine(directory.Path, "cache-destination")
        };
        var release = new TaskCompletionSource<(string, bool, CheckedCatalogBackup)?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        storage.RequestRestoreAsync = () => release.Task;
        var staging = storage.RestoreBackupCommand.ExecuteAsync(null);
        try
        {
            Assert.False(storage.ChangeCatalogCommand.CanExecute(null));
            Assert.False(storage.ChangeCacheCommand.CanExecute(null));
            Assert.False(storage.MoveCatalogCommand.CanExecute(null));
            Assert.False(storage.MoveCacheCommand.CanExecute(null));
            Assert.False(storage.RestoreBackupCommand.CanExecute(null));
        }
        finally
        {
            release.TrySetResult(null);
            await staging.WaitAsync(TestWaits.Condition);
        }
        Assert.True(storage.ChangeCatalogCommand.CanExecute(null));
        Assert.True(storage.ChangeCacheCommand.CanExecute(null));
        Assert.True(storage.MoveCatalogCommand.CanExecute(null));
        Assert.True(storage.MoveCacheCommand.CanExecute(null));
        Assert.True(storage.RestoreBackupCommand.CanExecute(null));
    }
}

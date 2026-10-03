using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BatchUndoViewModelTests
{
    [AvaloniaFact]
    public async Task DevelopEditAcceptedDuringUndoKeepsItsModelAndControls()
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        var release = Signal();
        fixture.Catalog.EditHistoryWriteGateAsync = () =>
        {
            fixture.Catalog.EditHistoryWriteGateAsync = null;

            return release.Task;
        };
        var undo = vm.UndoBatchCommand.ExecuteAsync(null);

        try
        {
            Assert.False(undo.IsCompleted);
            vm.SelectedImage = photos[1];
            vm.IsDevelopMode = true;
            await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.InitialPreviewActivityCount == 0);
            vm.Exposure = 3;
            fixture.AdvancePreviewClock();
            await TestWaits.UntilAsync(() => photos[1].EditSettings.Exposure == 3);
            release.SetResult();
            await undo.WaitAsync(TestWaits.Condition);
            await vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);

            Assert.Equal(3, vm.Exposure);
            Assert.Equal(3, photos[1].EditSettings.Exposure);
            var persisted = await fixture.Catalog.LoadImageStatesAsync([photos[1].FilePath]);
            Assert.Equal(3, Assert.Single(persisted[photos[1].FilePath]).EditSettings.Exposure);
            var history = await fixture.Catalog.LoadEditHistoryAsync(photos[1].CatalogId);
            Assert.Equal(3, history.Entries.Last().Settings.Exposure);
        }
        finally
        {
            release.TrySetResult();
            await undo;
        }
    }

    [AvaloniaFact]
    public async Task UndoWaitsForAcceptedDevelopSaveBeforeReadingEligibility()
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        vm.SelectedImage = photos[1];
        vm.IsDevelopMode = true;
        await vm.PendingHistoryLoadTask!;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.InitialPreviewActivityCount == 0);
        var entered = Signal();
        var release = Signal();
        fixture.Catalog.EditHistoryWriteGateAsync = () =>
        {
            fixture.Catalog.EditHistoryWriteGateAsync = null;
            entered.SetResult();

            return release.Task;
        };
        Task? undo = null;

        try
        {
            vm.Exposure = 2;
            fixture.AdvancePreviewClock();
            await entered.Task.WaitAsync(TestWaits.Condition);
            vm.IsDevelopMode = false;
            undo = vm.UndoBatchCommand.ExecuteAsync(null);
            Assert.False(undo.IsCompleted);
            release.SetResult();
            await undo.WaitAsync(TestWaits.Condition);

            Assert.Equal("Restored 10 of 11 · 1 changed since", vm.TransientStatus);
            Assert.Equal(2, photos[1].EditSettings.Exposure);
            var persisted = await fixture.Catalog.LoadImageStatesAsync([photos[1].FilePath]);
            Assert.Equal(2, Assert.Single(persisted[photos[1].FilePath]).EditSettings.Exposure);
            Assert.Equal(2, (await fixture.Catalog.LoadEditHistoryAsync(photos[1].CatalogId)).Position);
        }
        finally
        {
            release.TrySetResult();
            if (undo != null) await undo;
        }
    }

    [AvaloniaFact]
    public async Task FolderChangeDuringHeldBatchCannotPublishOffer()
    {
        await using var fixture = new SyncTransferParityVm();
        await PrepareAsync(fixture);
        var vm = fixture.Vm;
        var release = Signal();
        fixture.Catalog.EditHistoryWriteGateAsync = () => release.Task;
        var sync = vm.SyncSettingsCommand.ExecuteAsync(null);

        try
        {
            Assert.False(sync.IsCompleted);
            await vm.LoadFolderAsync(fixture.Catalog.CatalogPath);
            release.SetResult();
            await sync.WaitAsync(TestWaits.Condition);
            Assert.False(vm.IsBatchUndoOffered);
        }
        finally
        {
            release.TrySetResult();
            await sync;
        }
    }

    [AvaloniaFact]
    public async Task NewestBatchOwnsOfferWhenCompletionsOverlap()
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        vm.SelectedImage = photos[1];
        photos[0].EditSettings.Exposure = 2;
        vm.SelectedImage = photos[0];
        vm.CopyEditSettingsCommand.Execute(null);
        vm.SelectedImage = photos[1];
        photos[0].EditSettings.Exposure = 1;
        vm.SelectedImage = photos[0];
        var release = Signal();
        fixture.Catalog.EditHistoryWriteGateAsync = () =>
        {
            fixture.Catalog.EditHistoryWriteGateAsync = null;

            return release.Task;
        };
        var sync = vm.SyncSettingsCommand.ExecuteAsync(null);
        Task? paste = null;

        try
        {
            Assert.False(sync.IsCompleted);
            vm.Browse.SelectOnly(photos[1]);
            vm.RefreshSelectedCount();
            paste = vm.PasteEditSettingsCommand.ExecuteAsync(null);
            release.SetResult();
            await sync.WaitAsync(TestWaits.Condition);
            await paste.WaitAsync(TestWaits.Condition);
            Assert.Equal("Undo paste (1 photo)", vm.UndoBatchText);
            await vm.UndoBatchCommand.ExecuteAsync(null);
            Assert.False(vm.IsBatchUndoOffered);
        }
        finally
        {
            release.TrySetResult();
            await sync;
            if (paste != null) await paste;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoAndNewSyncRestoreConsistentDocumentsInEitherReleaseOrder(bool releaseSyncFirst)
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        var release = Signal();
        var releaseSync = Signal();
        var syncEntered = Signal();
        fixture.Catalog.EditHistoryWriteGateAsync = () =>
        {
            fixture.Catalog.EditHistoryWriteGateAsync = () =>
            {
                fixture.Catalog.EditHistoryWriteGateAsync = null;
                syncEntered.TrySetResult();

                return releaseSync.Task;
            };

            return release.Task;
        };
        var undo = vm.UndoBatchCommand.ExecuteAsync(null);
        Task? sync = null;

        try
        {
            Assert.False(undo.IsCompleted);
            vm.SelectedImage = photos[2];
            photos[0].EditSettings.Exposure = 2;
            vm.SelectedImage = photos[0];
            sync = vm.SyncSettingsCommand.ExecuteAsync(null);
            Assert.False(syncEntered.Task.IsCompleted);
            if (releaseSyncFirst) releaseSync.SetResult();

            release.SetResult();
            await undo.WaitAsync(TestWaits.Condition);
            await syncEntered.Task.WaitAsync(TestWaits.Condition);
            releaseSync.TrySetResult();
            await sync.WaitAsync(TestWaits.Condition);
            Assert.True(vm.IsBatchUndoOffered);
            Assert.Equal("Undo sync (11 photos)", vm.UndoBatchText);
            Assert.All(photos.Skip(1), image => Assert.Equal(2, image.EditSettings.Exposure));
            await vm.UndoBatchCommand.ExecuteAsync(null);
            Assert.All(photos.Skip(1), image => Assert.Equal(0, image.EditSettings.Exposure));
            var persisted = await fixture.Catalog.LoadImageStatesAsync(photos.Skip(1).Select(image => image.FilePath).ToArray());

            foreach (var image in photos.Skip(1))
            {
                Assert.Equal(EditSettingsJson.Serialize(image.EditSettings),
                    EditSettingsJson.Serialize(Assert.Single(persisted[image.FilePath]).EditSettings));
                Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(image.CatalogId)).Entries);
            }
        }
        finally
        {
            release.TrySetResult();
            releaseSync.TrySetResult();
            await undo;
            if (sync != null) await sync;
        }
    }

    [AvaloniaFact]
    public async Task UndoOfferedDuringSyncWaitsForItsModelPublication()
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        Task? undo = null;
        var waitedForPublication = false;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(vm.IsBatchUndoOffered) || !vm.IsBatchUndoOffered || undo != null) return;

            undo = vm.UndoBatchCommand.ExecuteAsync(null);
            waitedForPublication = !undo.IsCompleted;
        };

        await vm.SyncSettingsCommand.ExecuteAsync(null);
        Assert.NotNull(undo);
        await undo.WaitAsync(TestWaits.Condition);
        Assert.True(waitedForPublication);
        Assert.False(vm.IsBatchUndoOffered);
        var persisted = await fixture.Catalog.LoadImageStatesAsync(photos.Skip(1).Select(image => image.FilePath).ToArray());

        foreach (var image in photos.Skip(1))
        {
            Assert.Equal(0, image.EditSettings.Exposure);
            Assert.Equal(EditSettingsJson.Serialize(image.EditSettings),
                EditSettingsJson.Serialize(Assert.Single(persisted[image.FilePath]).EditSettings));
            Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(image.CatalogId)).Entries);
        }
    }

    [AvaloniaFact]
    public async Task ShutdownDrainsAcceptedUndo()
    {
        using var fixture = new CatalogVmFixture("batch-shutdown");
        using var catalog = await fixture.CreateCatalogAsync();
        var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        var source = new ImageFile(fixture.Path("source.jpg")) { EditSettings = new() { Exposure = 1 } };
        var target = new ImageFile(fixture.Path("target.jpg"));
        target.CatalogId = await catalog.GetOrCreateImageAsync(target.FilePath);
        vm.Browse.SetImages([source, target]);
        vm.SelectedImage = source;
        vm.SelectAllCommand.Execute(null);
        vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
        var release = Signal();
        Task? undo = null;
        Task? dispose = null;

        try
        {
            await vm.SyncSettingsCommand.ExecuteAsync(null);
            catalog.EditHistoryWriteGateAsync = () => release.Task;
            undo = vm.UndoBatchCommand.ExecuteAsync(null);
            Assert.False(undo.IsCompleted);
            dispose = vm.DisposeAsync().AsTask();
            Assert.False(dispose.IsCompleted);
            release.SetResult();
            await Task.WhenAll(undo, dispose).WaitAsync(TestWaits.Condition);
            var persisted = await catalog.LoadImageStatesAsync([target.FilePath]);
            Assert.Equal(0, Assert.Single(persisted[target.FilePath]).EditSettings.Exposure);
        }
        finally
        {
            release.TrySetResult();
            if (undo != null) await undo;
            await (dispose ?? vm.DisposeAsync().AsTask());
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

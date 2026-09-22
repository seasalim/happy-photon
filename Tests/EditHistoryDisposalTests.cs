using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditHistoryDisposalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownDrainsCurrentAndSupersededHistoryReads(bool supersede)
    {
        using var fixture = new CatalogVmFixture("history-disposal");
        using var catalog = await fixture.CreateCatalogAsync("catalog");
        var first = new ImageFile(fixture.Path("first.jpg"));
        first.CatalogId = await catalog.GetOrCreateImageAsync(first.FilePath);
        var second = new ImageFile(fixture.Path("second.jpg"));
        second.CatalogId = await catalog.GetOrCreateImageAsync(second.FilePath);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.EditHistoryLoadGateAsync = id =>
        {
            if (id != first.CatalogId) return Task.CompletedTask;
            started.TrySetResult();
            return release.Task;
        };
        var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        Task? heldLoad = null;
        Task? shutdown = null;
        try
        {
            vm.IsDevelopMode = true;
            vm.SelectedImage = first;
            await started.Task.WaitAsync(TestWaits.Condition);
            heldLoad = vm.PendingHistoryLoadTask!;
            if (supersede)
            {
                vm.SelectedImage = second;
                await vm.PendingHistoryLoadTask!.WaitAsync(TestWaits.Condition);
            }
            bool? readFinishedBeforeServiceTeardown = null;
            vm.DependentExportServicesDisposing += () =>
                readFinishedBeforeServiceTeardown = heldLoad.IsCompleted;

            shutdown = vm.DisposeAsync().AsTask();

            Assert.False(shutdown.IsCompleted);
            Assert.Null(readFinishedBeforeServiceTeardown);
            release.TrySetResult();
            await shutdown.WaitAsync(TestWaits.Condition);
            Assert.True(readFinishedBeforeServiceTeardown);
            catalog.Dispose();
            Directory.Delete(catalog.CatalogPath, recursive: true);
        }
        finally
        {
            release.TrySetResult();
            if (heldLoad != null) await heldLoad.WaitAsync(TestWaits.Condition);
            if (vm.PendingHistoryLoadTask is { } latest)
                await latest.WaitAsync(TestWaits.Condition);
            await (shutdown ?? vm.DisposeAsync().AsTask()).WaitAsync(TestWaits.Condition);
        }
    }

    [Fact]
    public async Task ShutdownPreservesAnAcceptedEditWaitingForHistory()
    {
        using var fixture = new CatalogVmFixture("history-disposal-edit");
        using var catalog = await fixture.CreateCatalogAsync();
        var image = new ImageFile(fixture.Path("photo.jpg"))
        {
            EditSettings = new EditSettings { Exposure = 1 }
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsWithHistoryAsync(
            image.CatalogId, image.EditSettings,
            new CatalogEditHistoryMutation(
                -1, [new CatalogEditHistoryEntry(0, "Original", image.EditSettings.Clone())], 0));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.EditHistoryLoadGateAsync = _ =>
        {
            started.TrySetResult();
            return release.Task;
        };
        var vm = fixture.CreateViewModel(catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        Task? save = null;
        Task? shutdown = null;
        try
        {
            vm.IsDevelopMode = true;
            vm.SelectedImage = image;
            await started.Task.WaitAsync(TestWaits.Condition);
            var before = image.EditSettings.Clone();
            image.EditSettings.Exposure = 2;
            var saveMethod = typeof(MainWindowViewModel).GetMethod(
                "SaveEditSettingsAsync", BindingFlags.Instance | BindingFlags.NonPublic,
                null, [typeof(ImageFile), typeof(string), typeof(EditSettings)], null)!;
            save = (Task)saveMethod.Invoke(vm, [image, "Exposure +2.00", before])!;

            shutdown = vm.DisposeAsync().AsTask();
            Assert.False(save.IsCompleted);
            Assert.False(shutdown.IsCompleted);
            release.TrySetResult();
            await shutdown.WaitAsync(TestWaits.Condition);
            await save.WaitAsync(TestWaits.Condition);

            var stored = await catalog.LoadEditHistoryAsync(image.CatalogId);
            Assert.Equal([1d, 2d], stored.Entries.Select(entry => entry.Settings.Exposure));
            Assert.Equal(1, stored.Position);
            var state = Assert.Single((await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath]);
            Assert.Equal(2, state.EditSettings.Exposure);
        }
        finally
        {
            release.TrySetResult();
            if (save != null) await save.WaitAsync(TestWaits.Condition);
            if (vm.PendingHistoryLoadTask is { } load)
                await load.WaitAsync(TestWaits.Condition);
            await (shutdown ?? vm.DisposeAsync().AsTask()).WaitAsync(TestWaits.Condition);
        }
    }
}

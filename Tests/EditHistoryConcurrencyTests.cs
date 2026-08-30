using System.Reflection;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditHistoryConcurrencyTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("history-concurrency");

    [Fact]
    public async Task RapidAtoBtoCSelectionPublishesOnlyC()
    {
        using var catalog = await _fixture.CreateCatalogAsync("abc");
        var a = await SeedAsync(catalog, "a.jpg", 1);
        var b = await SeedAsync(catalog, "b.jpg", 2);
        var c = await SeedAsync(catalog, "c.jpg", 3);
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = false;
        catalog.EditHistoryLoadGateAsync = id =>
        {
            if (id != a.CatalogId || blocked) return Task.CompletedTask;
            blocked = true;
            firstStarted.TrySetResult();
            return releaseFirst.Task;
        };
        await using var vm = CreateViewModel(catalog);
        vm.Browse.SetImages([a, b, c]);

        vm.SelectedImage = a;
        await firstStarted.Task.WaitAsync(TestWaits.Condition);
        var firstLoad = Assert.IsAssignableFrom<Task>(vm.PendingHistoryLoadTask);
        vm.SelectedImage = b;
        vm.SelectedImage = c;
        await TestWaits.UntilAsync(() =>
            vm.IsHistoryLoaded && Current(vm).Label == "Exposure +3.00");
        releaseFirst.TrySetResult();
        await firstLoad;

        Assert.Same(c, vm.SelectedImage);
        Assert.Equal("Exposure +3.00", Current(vm).Label);
    }

    [Fact]
    public async Task RapidAtoBtoASelectionRejectsTheFirstALoad()
    {
        using var catalog = await _fixture.CreateCatalogAsync("aba");
        var a = await SeedAsync(catalog, "a.jpg", 1);
        var b = await SeedAsync(catalog, "b.jpg", 2);
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var aCalls = 0;
        catalog.EditHistoryLoadGateAsync = id =>
        {
            if (id != a.CatalogId || Interlocked.Increment(ref aCalls) != 1)
                return Task.CompletedTask;
            firstStarted.TrySetResult();
            return releaseFirst.Task;
        };
        await using var vm = CreateViewModel(catalog);
        vm.Browse.SetImages([a, b]);

        vm.SelectedImage = a;
        await firstStarted.Task.WaitAsync(TestWaits.Condition);
        var firstLoad = Assert.IsAssignableFrom<Task>(vm.PendingHistoryLoadTask);
        vm.SelectedImage = b;
        vm.SelectedImage = a;
        await TestWaits.UntilAsync(() =>
            vm.IsHistoryLoaded && Current(vm).Label == "Exposure +1.00");
        releaseFirst.TrySetResult();
        await firstLoad;

        Assert.Same(a, vm.SelectedImage);
        Assert.Equal("Exposure +1.00", Current(vm).Label);
    }

    [Fact]
    public async Task EditDuringLoadAppendsAfterLoadedRows()
    {
        using var catalog = await _fixture.CreateCatalogAsync("edit-load");
        var image = await SeedAsync(catalog, "photo.jpg", 1);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.EditHistoryLoadGateAsync = _ =>
        {
            started.TrySetResult();
            return release.Task;
        };
        await using var vm = CreateViewModel(catalog);
        vm.SelectedImage = image;
        await started.Task.WaitAsync(TestWaits.Condition);
        var before = image.EditSettings.Clone();
        image.EditSettings.Exposure = 2;

        var save = InvokeHistorySave(vm, image, "Exposure +2.00", before);
        Assert.False(save.IsCompleted);
        release.TrySetResult();
        await save;

        Assert.Equal(
            ["Original", "Exposure +1.00", "Exposure +2.00"],
            vm.HistoryEntries.Reverse().Select(entry => entry.Label));
        Assert.Equal("Exposure +2.00", Current(vm).Label);
    }

    [Fact]
    public async Task EmptyHistoryReportsReadinessFalseThenTrue()
    {
        using var catalog = await _fixture.CreateCatalogAsync("readiness");
        var image = await CreateImageAsync(catalog, "empty.jpg");
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.EditHistoryLoadGateAsync = _ =>
        {
            started.TrySetResult();
            return release.Task;
        };
        await using var vm = CreateViewModel(catalog);

        vm.SelectedImage = image;
        await started.Task.WaitAsync(TestWaits.Condition);
        Assert.False(vm.IsHistoryLoaded);
        release.TrySetResult();
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);

        Assert.Empty(vm.HistoryEntries);
    }

    [Fact]
    public async Task FaultedLoadLeavesRowsUntouchedAndRetriesOnReselect()
    {
        using var catalog = await _fixture.CreateCatalogAsync("load-failure");
        var image = await SeedAsync(catalog, "fault.jpg", 1);
        var other = await CreateImageAsync(catalog, "other.jpg");
        catalog.EditHistoryLoadGateAsync = _ =>
            Task.FromException(new IOException("Injected load failure"));
        await using var vm = CreateViewModel(catalog);
        vm.Browse.SetImages([image, other]);

        vm.SelectedImage = image;
        await Assert.IsAssignableFrom<Task>(vm.PendingHistoryLoadTask);
        Assert.False(vm.IsHistoryLoaded);
        Assert.Empty(vm.HistoryEntries);
        var before = image.EditSettings.Clone();
        image.EditSettings.Exposure = 2;
        await InvokeHistorySave(vm, image, "Exposure +2.00", before);

        catalog.EditHistoryLoadGateAsync = null;
        var persisted = await catalog.LoadEditHistoryAsync(image.CatalogId);
        Assert.Equal(["Original", "Exposure +1.00"],
            persisted.Entries.Select(entry => entry.Label));

        vm.SelectedImage = other;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        Assert.Equal(["Exposure +1.00", "Original"],
            vm.HistoryEntries.Select(entry => entry.Label));
    }

    [Fact]
    public async Task BackToBackCommitsAreSerializedWithCapturedSnapshots()
    {
        using var catalog = await _fixture.CreateCatalogAsync("serialized");
        var image = await SeedAsync(catalog, "serialized.jpg", 1);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.EditHistoryWriteGateAsync = () =>
        {
            started.TrySetResult();
            return release.Task;
        };
        await using var vm = CreateViewModel(catalog);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);

        image.EditSettings.Exposure = 2;
        var first = InvokeHistorySave(vm, image);
        await started.Task.WaitAsync(TestWaits.Condition);
        image.EditSettings.Exposure = 3;
        var second = InvokeHistorySave(vm, image);
        Assert.False(second.IsCompleted);

        release.TrySetResult();
        await Task.WhenAll(first, second);

        var state = await catalog.LoadEditHistoryAsync(image.CatalogId);
        Assert.Equal([0d, 1d, 2d, 3d],
            state.Entries.Select(entry => entry.Settings.Exposure));
        Assert.Equal(
            ["Original", "Exposure +1.00", "Exposure +2.00 (+1.00)",
             "Exposure +3.00 (+1.00)"],
            state.Entries.Select(entry => entry.Label));
    }

    [Fact]
    public async Task JumpCompletingAfterSelectionChangeDoesNotMoveNewSubject()
    {
        using var catalog = await _fixture.CreateCatalogAsync("stale-jump");
        var a = await SeedAsync(catalog, "a.jpg", 2);
        var b = await SeedAsync(catalog, "b.jpg", 1);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.EditHistoryWriteGateAsync = () =>
        {
            started.TrySetResult();
            return release.Task;
        };
        await using var vm = CreateViewModel(catalog);
        vm.Browse.SetImages([a, b]);
        vm.SelectedImage = a;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);

        var jump = vm.JumpToHistoryStepCommand.ExecuteAsync(vm.HistoryEntries[^1]);
        await started.Task.WaitAsync(TestWaits.Condition);
        vm.SelectedImage = b;
        await TestWaits.UntilAsync(() =>
            vm.IsHistoryLoaded && Current(vm).Label == "Exposure +1.00");
        release.TrySetResult();
        await jump;

        Assert.Same(b, vm.SelectedImage);
        Assert.Equal("Exposure +1.00", Current(vm).Label);
        Assert.Equal(1, b.EditSettings.Exposure);
    }

    [Fact]
    public async Task TrimPublishesAfterOneWriteAndRemainsUndoable()
    {
        using var catalog = await _fixture.CreateCatalogAsync("trim-gate");
        var image = await SeedAsync(catalog, "trim.jpg", 3);
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        catalog.EditHistoryWriteGateAsync = () =>
        {
            writes++;
            started.TrySetResult();
            return release.Task;
        };
        await using var vm = CreateViewModel(catalog);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        var labels = vm.HistoryEntries.Select(entry => entry.Label).ToArray();
        var current = Current(vm);
        var target = vm.HistoryEntries.Single(
            entry => entry.Label == "Exposure +1.00");

        var trim = vm.ClearHistoryAboveStepCommand.ExecuteAsync(target);
        await started.Task.WaitAsync(TestWaits.Condition);

        Assert.Equal(1, writes);
        Assert.Equal(labels, vm.HistoryEntries.Select(entry => entry.Label));
        Assert.Same(current, Current(vm));
        Assert.True(vm.CanUndo);
        Assert.False(vm.CanRedo);
        var blockedState = await catalog.LoadEditHistoryAsync(image.CatalogId);
        Assert.Equal(4, blockedState.Entries.Count);
        Assert.Equal(3, blockedState.Position);

        release.TrySetResult();
        await trim;

        Assert.Equal(["Exposure +1.00", "Original"],
            vm.HistoryEntries.Select(entry => entry.Label));
        Assert.Equal(1, image.EditSettings.Exposure);
        Assert.True(vm.CanUndo);
        Assert.False(vm.CanRedo);
        var trimmedState = await catalog.LoadEditHistoryAsync(image.CatalogId);
        Assert.Equal([0d, 1d],
            trimmedState.Entries.Select(entry => entry.Settings.Exposure));
        Assert.Equal(1, trimmedState.Position);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, image.EditSettings.Exposure);
        Assert.Equal(0, (await catalog.LoadEditHistoryAsync(image.CatalogId)).Position);
        await vm.RedoCommand.ExecuteAsync(null);
        Assert.Equal(1, image.EditSettings.Exposure);
        Assert.Equal(1, (await catalog.LoadEditHistoryAsync(image.CatalogId)).Position);
    }

    [Fact]
    public async Task TrimFailureLeavesMemoryAndCatalogUnchanged()
    {
        using var catalog = await _fixture.CreateCatalogAsync("trim-failure");
        var image = await SeedAsync(catalog, "trim-failure.jpg", 3);
        await using var vm = CreateViewModel(catalog);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        var labels = vm.HistoryEntries.Select(entry => entry.Label).ToArray();
        var current = Current(vm);
        var target = vm.HistoryEntries.Single(
            entry => entry.Label == "Exposure +1.00");
        catalog.EditHistoryWriteGateAsync = () =>
            Task.FromException(new IOException("Injected trim failure"));

        await Assert.ThrowsAsync<IOException>(() =>
            vm.ClearHistoryAboveStepCommand.ExecuteAsync(target));

        Assert.Equal(labels, vm.HistoryEntries.Select(entry => entry.Label));
        Assert.Same(current, Current(vm));
        Assert.Equal(3, image.EditSettings.Exposure);
        Assert.True(vm.CanUndo);
        Assert.False(vm.CanRedo);
        var state = await catalog.LoadEditHistoryAsync(image.CatalogId);
        Assert.Equal([0d, 1d, 2d, 3d],
            state.Entries.Select(entry => entry.Settings.Exposure));
        Assert.Equal(3, state.Position);
    }

    [Fact]
    public async Task AppendJumpAndClearFailuresLeavePublishedHistoryUnchanged()
    {
        using var catalog = await _fixture.CreateCatalogAsync("write-failure");
        var image = await SeedAsync(catalog, "failure.jpg", 2);
        await using var vm = CreateViewModel(catalog);
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded);
        var labels = vm.HistoryEntries.Select(entry => entry.Label).ToArray();
        var current = Current(vm);
        catalog.EditHistoryWriteGateAsync = () =>
            Task.FromException(new IOException("Injected write failure"));

        var before = image.EditSettings.Clone();
        image.EditSettings.Exposure = 3;
        await Assert.ThrowsAsync<IOException>(() =>
            InvokeHistorySave(vm, image, "Exposure +3.00", before));
        Assert.Equal(labels, vm.HistoryEntries.Select(entry => entry.Label));
        Assert.Same(current, Current(vm));
        image.EditSettings = before;

        await Assert.ThrowsAsync<IOException>(() =>
            vm.JumpToHistoryStepCommand.ExecuteAsync(vm.HistoryEntries[^1]));
        Assert.Equal(labels, vm.HistoryEntries.Select(entry => entry.Label));
        Assert.Same(current, Current(vm));

        await Assert.ThrowsAsync<IOException>(() =>
            vm.ClearHistoryCommand.ExecuteAsync(null));
        Assert.Equal(labels, vm.HistoryEntries.Select(entry => entry.Label));
        Assert.Same(current, Current(vm));
    }

    private MainWindowViewModel CreateViewModel(CatalogService catalog)
    {
        var vm = _fixture.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        return vm;
    }

    private async Task<ImageFile> CreateImageAsync(
        CatalogService catalog,
        string name,
        EditSettings? settings = null)
    {
        var image = new ImageFile(_fixture.Path(name))
        {
            EditSettings = settings ?? new EditSettings()
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        return image;
    }

    private async Task<ImageFile> SeedAsync(
        CatalogService catalog,
        string name,
        int exposure)
    {
        var current = new EditSettings { Exposure = exposure };
        var image = await CreateImageAsync(catalog, name, current);
        var entries = Enumerable.Range(0, exposure + 1)
            .Select(index => new CatalogEditHistoryEntry(
                index,
                index == 0 ? "Original" : $"Exposure +{index}.00",
                new EditSettings { Exposure = index }))
            .ToArray();
        await catalog.SaveEditSettingsWithHistoryAsync(
            image.CatalogId,
            current,
            new CatalogEditHistoryMutation(-1, entries, exposure));
        return image;
    }

    private static Task InvokeHistorySave(
        MainWindowViewModel vm,
        ImageFile image,
        string label,
        EditSettings before)
    {
        var save = typeof(MainWindowViewModel).GetMethod(
            "SaveEditSettingsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(ImageFile), typeof(string), typeof(EditSettings)],
            null)!;
        return (Task)save.Invoke(vm, [image, label, before])!;
    }

    private static Task InvokeHistorySave(
        MainWindowViewModel vm,
        ImageFile image)
    {
        var save = typeof(MainWindowViewModel).GetMethod(
            "SaveEditSettingsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(ImageFile)],
            null)!;
        return (Task)save.Invoke(vm, [image])!;
    }

    private static EditHistoryEntry Current(MainWindowViewModel vm) =>
        Assert.Single(vm.HistoryEntries, entry => entry.IsCurrent);

    public void Dispose() => _fixture.Dispose();
}

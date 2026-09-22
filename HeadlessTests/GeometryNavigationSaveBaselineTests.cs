using System.Text.Json;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

// WP260 assertions describe post-fix behavior; deterministic failures are not flaky quarantine.
public sealed class GeometryNavigationSaveBaselineTests(ITestOutputHelper output) : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("geometry-navigation-baseline");

    [AvaloniaTheory]
    [InlineData("rotation", false)]
    [InlineData("crop", false)]
    [InlineData("locals", false)]
    [InlineData("rotation", true)]
    [InlineData("crop", true)]
    [InlineData("locals", true)]
    public async Task CompletedCommitPersistsOneHistoryStepAfterNavigation(string operation, bool redoTail)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        var source = await CreateImageAsync(catalog, "source.jpg", .2);
        if (redoTail)
        {
            var tail = new EditSettings { Exposure = .7 };
            await catalog.SaveEditSettingsWithHistoryAsync(source.CatalogId, source.EditSettings,
                new CatalogEditHistoryMutation(1, [new(2, "Redo tail", tail)], 1));
        }
        var destination = await CreateImageAsync(catalog, "destination.jpg", .4);
        vm.Browse.SetImages([source, destination]);
        vm.SelectedImage = source;
        await ReadyAsync(vm);
        await PrepareOperationAsync(vm, clock, operation);
        var sourceHistory = await catalog.LoadEditHistoryAsync(source.CatalogId);
        var destinationBefore = await SnapshotAsync(catalog, destination);
        var started = NewSignal();
        var release = NewSignal();
        var calls = 0;
        vm.ImageService.Previews.RenderGateAsync = () =>
        {
            if (Interlocked.Increment(ref calls) != 1) return Task.CompletedTask;
            started.TrySetResult();
            return release.Task;
        };
        try
        {
            var completed = Commit(vm, operation);
            await started.Task.WaitAsync(TestWaits.Condition);
            var expected = source.EditSettings.Clone();
            if (operation == "rotation") Assert.Equal(90, expected.Rotation);
            else if (operation == "crop")
            {
                Assert.Equal(2, expected.HorizonRotation);
                Assert.Equal(.1, expected.Crop!.Left);
                Assert.Equal(.2, expected.Crop.Top);
                Assert.Equal(.8, expected.Crop.Right);
                Assert.Equal(.9, expected.Crop.Bottom);
            }
            else Assert.Single(expected.Locals!);
            vm.SelectedImage = destination;
            Assert.Same(destination, vm.SelectedImage);
            await ReadyAsync(vm);
            release.TrySetResult();
            await completed.WaitAsync(TestWaits.Condition);
            await ReadyAsync(vm);
            var persisted = await SettingsAsync(catalog, source);
            var history = await catalog.LoadEditHistoryAsync(source.CatalogId);
            var destinationAfter = await SnapshotAsync(catalog, destination);
            var destinationUnchanged = destinationBefore == destinationAfter &&
                destination.EditSettings.HasSameEdits(destinationBefore.Settings);
            output.WriteLine($"{operation}: rotation={persisted.Rotation}; " +
                $"crop={JsonSerializer.Serialize(persisted.Crop)}; horizon={persisted.HorizonRotation}; " +
                $"locals={persisted.Locals?.Count ?? 0}; settingsMatch={persisted.HasSameEdits(expected)}; " +
                $"history={sourceHistory.Entries.Count}->{history.Entries.Count}; " +
                $"delta={history.Entries.Count - sourceHistory.Entries.Count}; redoTail={redoTail}; " +
                $"destinationUnchanged={destinationUnchanged}");
            Assert.True(destinationUnchanged);
            Assert.Equal(destinationBefore.HistoryCount, vm.HistoryEntries.Count);
            Assert.True(vm.HistoryEntries.Single(entry => entry.IsCurrent)
                .Settings.HasSameEdits(destination.EditSettings));
            Assert.True(persisted.HasSameEdits(expected), "Completed settings must persist to the source.");
            Assert.Equal(sourceHistory.Position + 2, history.Entries.Count);
            Assert.Equal(sourceHistory.Position + 1, history.Position);
            Assert.True(history.Entries[^1].Settings.HasSameEdits(expected));
            Assert.DoesNotContain(history.Entries, entry => entry.Label == "Redo tail");
        }
        finally
        {
            release.TrySetResult();
            vm.ImageService.Previews.RenderGateAsync = null;
        }
    }

    [AvaloniaTheory]
    [InlineData("rotation")]
    [InlineData("crop")]
    public async Task CurrentRenderFailureRollsBackBeforeNavigation(string operation)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        var source = await CreateImageAsync(catalog, "source.jpg", .2);
        var destination = await CreateImageAsync(catalog, "destination.jpg", .4);
        vm.Browse.SetImages([source, destination]);
        vm.SelectedImage = source;
        await ReadyAsync(vm);
        var sourceBefore = await SnapshotAsync(catalog, source);
        var destinationBefore = await SnapshotAsync(catalog, destination);
        await PrepareOperationAsync(vm, clock, operation);
        var failures = 0;
        vm.ImageService.Previews.RenderGateAsync = () =>
        {
            Interlocked.Increment(ref failures);
            return Task.FromException(new InvalidOperationException("WP260 current render failure"));
        };
        await Commit(vm, operation).WaitAsync(TestWaits.Condition);
        var revertedWhileCurrent = ReferenceEquals(vm.SelectedImage, source) &&
            source.EditSettings.HasSameEdits(sourceBefore.Settings);
        vm.ImageService.Previews.RenderGateAsync = null;
        vm.SelectedImage = destination;
        await ReadyAsync(vm);
        var sourceAfter = await SnapshotAsync(catalog, source);
        var destinationAfter = await SnapshotAsync(catalog, destination);
        var destinationUnchanged = destinationBefore == destinationAfter &&
            destination.EditSettings.HasSameEdits(destinationBefore.Settings);
        output.WriteLine($"{operation}: failures={failures}; revertedWhileCurrent={revertedWhileCurrent}; " +
            $"persistedRotation={sourceAfter.Settings.Rotation}; " +
            $"persistedCrop={JsonSerializer.Serialize(sourceAfter.Settings.Crop)}; " +
            $"persistedHorizon={sourceAfter.Settings.HorizonRotation}; " +
            $"sourceUnchanged={sourceBefore == sourceAfter}; " +
            $"history={sourceBefore.HistoryCount}->{sourceAfter.HistoryCount}; " +
            $"destinationUnchanged={destinationUnchanged}");
        Assert.True(failures > 0);
        Assert.True(revertedWhileCurrent);
        Assert.Equal(sourceBefore, sourceAfter);
        Assert.True(destinationUnchanged);
    }

    [AvaloniaTheory]
    [InlineData("rotation")]
    [InlineData("crop")]
    [InlineData("locals")]
    public async Task ReturningBeforeWriteLoadsTheCompletedHistory(string operation)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        var source = await CreateImageAsync(catalog, "source.jpg", .2);
        var destination = await CreateImageAsync(catalog, "destination.jpg", .4);
        vm.Browse.SetImages([source, destination]);
        vm.SelectedImage = source;
        await ReadyAsync(vm);
        await PrepareOperationAsync(vm, clock, operation);
        var sourceHistory = await catalog.LoadEditHistoryAsync(source.CatalogId);
        var destinationBefore = await SnapshotAsync(catalog, destination);
        var started = NewSignal();
        var release = NewSignal();
        var writesCompleted = false;
        var prematureReads = 0;
        catalog.EditHistoryWriteGateAsync = async () =>
        {
            started.TrySetResult();
            await release.Task;
            writesCompleted = true;
        };
        try
        {
            var completed = Commit(vm, operation);
            await started.Task.WaitAsync(TestWaits.Condition);
            var expected = source.EditSettings.Clone();
            catalog.EditHistoryLoadGateAsync = id =>
            {
                if (id == source.CatalogId && !Volatile.Read(ref writesCompleted))
                    Interlocked.Increment(ref prematureReads);
                return Task.CompletedTask;
            };
            vm.SelectedImage = destination;
            vm.SelectedImage = source;
            var load = Assert.IsAssignableFrom<Task>(vm.PendingHistoryLoadTask);
            Assert.False(vm.IsHistoryLoaded);
            release.TrySetResult();
            await completed.WaitAsync(TestWaits.Condition);
            await load.WaitAsync(TestWaits.Condition);
            var history = await catalog.LoadEditHistoryAsync(source.CatalogId);
            output.WriteLine($"{operation}: persistedHistory={history.Entries.Count}; " +
                $"loadedHistory={vm.HistoryEntries.Count}; prematureReads={prematureReads}");
            Assert.Equal(0, prematureReads);
            Assert.Equal(sourceHistory.Position + 2, history.Entries.Count);
            Assert.Equal(history.Entries.Count, vm.HistoryEntries.Count);
            Assert.True(vm.HistoryEntries.Single(entry => entry.IsCurrent)
                .Settings.HasSameEdits(expected));
            Assert.Equal(destinationBefore, await SnapshotAsync(catalog, destination));
        }
        finally
        {
            release.TrySetResult();
            catalog.EditHistoryWriteGateAsync = null;
            catalog.EditHistoryLoadGateAsync = null;
        }
    }

    [AvaloniaFact]
    public async Task ReturningToFolderWaitsForRotationBeforeReplacingImage()
    {
        using var catalog = await _fixture.CreateCatalogAsync("catalog");
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        var folder = Directory.CreateDirectory(_fixture.Path("source")).FullName;
        Directory.CreateDirectory(Path.Combine(folder, "child"));
        var otherFolder = Directory.CreateDirectory(_fixture.Path("other")).FullName;
        var source = await CreateImageAsync(catalog, Path.Combine("source", "source.jpg"), .2);
        TestImages.WriteJpeg(source.FilePath);
        await vm.LoadFolderAsync(folder).WaitAsync(TestWaits.Condition);
        vm.SelectedImage = source = Assert.Single(vm.Browse.AllImages);
        await ReadyAsync(vm);
        var historyBefore = await catalog.LoadEditHistoryAsync(source.CatalogId);
        var started = NewSignal();
        var release = NewSignal();
        var calls = 0;
        vm.ImageService.Previews.RenderGateAsync = () =>
        {
            if (Interlocked.Increment(ref calls) != 1) return Task.CompletedTask;
            started.TrySetResult();
            return release.Task;
        };
        try
        {
            var completed = Commit(vm, "rotation");
            await started.Task.WaitAsync(TestWaits.Condition);
            await vm.LoadFolderAsync(otherFolder).WaitAsync(TestWaits.Condition);
            Assert.Empty(vm.Browse.AllImages);
            var returning = vm.LoadFolderAsync(folder);
            await TestWaits.UntilAsync(() => vm.CurrentFolderHasSubfolders);
            // Settle only to observe absence: the folder must not publish stale state
            // while the render remains gated. A slower runner widens this check.
            await Task.Delay(100);
            var returnedBeforeSave = returning.IsCompleted;
            release.TrySetResult();
            await completed.WaitAsync(TestWaits.Condition);
            await returning.WaitAsync(TestWaits.Condition);
            var returned = Assert.Single(vm.Browse.AllImages);
            vm.SelectedImage = returned;
            await ReadyAsync(vm);
            output.WriteLine($"folder return: premature={returnedBeforeSave}; " +
                $"rotation={returned.EditSettings.Rotation}; " +
                $"history={historyBefore.Entries.Count}->{vm.HistoryEntries.Count}");
            Assert.NotSame(source, returned);
            Assert.Equal(source.CatalogId, returned.CatalogId);
            Assert.Equal(90, returned.EditSettings.Rotation);
            Assert.False(returnedBeforeSave);
            Assert.Equal(historyBefore.Entries.Count + 1, vm.HistoryEntries.Count);
            Assert.Equal(90, vm.HistoryEntries.Single(entry => entry.IsCurrent).Settings.Rotation);

            await Commit(vm, "rotation").WaitAsync(TestWaits.Condition);
            var persisted = await SettingsAsync(catalog, returned);
            var history = await catalog.LoadEditHistoryAsync(returned.CatalogId);
            output.WriteLine($"second rotation: rotation={persisted.Rotation}; " +
                $"history={historyBefore.Entries.Count}->{history.Entries.Count}");
            Assert.Equal(180, returned.EditSettings.Rotation);
            Assert.Equal(180, persisted.Rotation);
            Assert.Equal(historyBefore.Entries.Count + 2, history.Entries.Count);
            Assert.Equal(history.Entries.Count, vm.HistoryEntries.Count);
            Assert.Equal(180, history.Entries[^1].Settings.Rotation);
        }
        finally
        {
            release.TrySetResult();
            vm.ImageService.Previews.RenderGateAsync = null;
        }
    }
    private MainWindowViewModel CreateVm(CatalogService catalog, TestTimeProvider clock)
    {
        var vm = _fixture.CreateViewModel(catalog, new LocalTestLoader(),
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: clock);
        vm.IsDevelopMode = true;
        return vm;
    }

    private async Task<ImageFile> CreateImageAsync(CatalogService catalog, string name, double exposure)
    {
        var image = new ImageFile(_fixture.Path(name))
        {
            EditSettings = new EditSettings { Exposure = exposure }
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        // Seed a prior edit including Original, so the measured delta is exactly one row.
        var mutation = CatalogEditHistory.PrepareAppend(new([], -1),
            new EditSettings(), image.EditSettings);
        await catalog.SaveEditSettingsWithHistoryAsync(image.CatalogId, image.EditSettings, mutation);
        return image;
    }

    private static async Task PrepareOperationAsync(
        MainWindowViewModel vm, TestTimeProvider clock, string operation)
    {
        if (operation == "crop")
        {
            await vm.ToggleCropModeCommand.ExecuteAsync(null);
            vm.HorizonRotation = 2;
            clock.Advance(TimeSpan.FromMilliseconds(200));
            if (vm.PendingPreviewDebounceTask is { } preview)
                await preview.WaitAsync(TestWaits.Condition);
            if (vm.PendingHistoryCommitTask is { } commit)
                await commit.WaitAsync(TestWaits.Condition);
            vm.CurrentCrop = new CropRegion { Left = .1, Top = .2, Right = .8, Bottom = .9 };
        }
        else if (operation == "locals")
        {
            await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
            vm.AddLinearCommand.Execute(null);
            Assert.True(vm.BeginLocalsGesture(LocalHandle.Create, new(.2, .2)));
            vm.MoveLocalsGesture(new(.8, .8), 100);
        }
    }

    private static Task Commit(MainWindowViewModel vm, string operation)
    {
        if (operation == "crop") return vm.ApplyCropCommand.ExecuteAsync(null);
        if (operation == "locals") return vm.CompleteLocalsGestureAsync();
        vm.RotateRightCommand.Execute(null);
        return Assert.IsAssignableFrom<Task>(vm.PendingHistoryCommitTask);
    }

    private static Task ReadyAsync(MainWindowViewModel vm) =>
        TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);

    private static async Task<EditSettings> SettingsAsync(CatalogService catalog, ImageFile image) =>
        (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single().EditSettings;

    private static async Task<Snapshot> SnapshotAsync(CatalogService catalog, ImageFile image)
    {
        var settings = await SettingsAsync(catalog, image);
        var history = await catalog.LoadEditHistoryAsync(image.CatalogId);
        return new(JsonSerializer.Serialize(settings), JsonSerializer.Serialize(history), history.Entries.Count);
    }

    private sealed record Snapshot(string SettingsJson, string HistoryJson, int HistoryCount)
    {
        public EditSettings Settings => JsonSerializer.Deserialize<EditSettings>(SettingsJson)!;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Dispose() => _fixture.Dispose();
}

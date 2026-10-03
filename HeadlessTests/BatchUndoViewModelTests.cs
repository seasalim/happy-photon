using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BatchUndoViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ButtonAndShortcutRoundTripElevenTargets(bool paste, bool loupe)
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        vm.CopyEditSettingsCommand.Execute(null);
        var buffer = typeof(MainWindowViewModel).GetField("_copiedSettings", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(vm);
        var original = photos.Skip(1).Select(image => EditSettingsJson.Serialize(image.EditSettings)).ToArray();

        if (paste)
        {
            vm.Browse.ToggleSelection(photos[0]);
            vm.SelectedImage = photos[0];
            await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        }
        else
        {
            await vm.SyncSettingsCommand.ExecuteAsync(null);
        }

        var thumbnailRefreshes = 0;
        vm.ImageService.Thumbnails.SourceLoadGateAsync = () =>
        {
            Interlocked.Increment(ref thumbnailRefreshes);

            return Task.CompletedTask;
        };
        var groups = vm.CapturePasteGroups();
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        if (loupe) vm.EnterLoupeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var button = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "UndoSyncButton");
        Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled);
        Assert.Equal($"Undo {(paste ? "paste" : "sync")} (11 photos)", button.Content);
        Assert.Same(vm.UndoBatchCommand, button.Command);
        Assert.Same(vm.UndoCommand, Assert.Single(window.KeyBindings,
            binding => binding.Gesture.ToString() == "Ctrl+Z").Command);
        vm.TransientStatus = null;
        Assert.True(button.IsEffectivelyVisible);
        vm.TransientStatus = "Other activity";
        Assert.True(button.IsEffectivelyVisible);

        if (loupe)
        {
            window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
            window.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.None, null);
            await TestWaits.UntilAsync(() => !vm.IsBatchUndoOffered);
        }
        else
        {
            await vm.UndoBatchCommand.ExecuteAsync(null);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.False(button.IsEffectivelyVisible);
        Assert.False(vm.UndoBatchCommand.CanExecute(null));
        Assert.False(vm.UndoCommand.CanExecute(null));
        Assert.Equal("Restored 11 photos", vm.TransientStatus);
        await TestWaits.UntilAsync(() => Volatile.Read(ref thumbnailRefreshes) >= 11);
        Assert.True(vm.HasCopiedSettings);
        Assert.Same(buffer, typeof(MainWindowViewModel).GetField("_copiedSettings", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(vm));
        Assert.Equal(groups, vm.CapturePasteGroups());

        for (var index = 1; index < photos.Length; index++)
        {
            Assert.Equal(original[index - 1], EditSettingsJson.Serialize(photos[index].EditSettings));
            Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(photos[index].CatalogId)).Entries);
        }

        Assert.Equal(1, photos[0].EditSettings.Exposure);
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(photos[0].CatalogId)).Entries);
        vm.ExitLoupeCommand.Execute(null);
        vm.SelectedImage = photos[1];
        vm.IsDevelopMode = true;
        await vm.PendingHistoryLoadTask!;
        Assert.False(vm.RedoCommand.CanExecute(null));
    }

    [AvaloniaTheory]
    [InlineData("edit", false)]
    [InlineData("undo", false)]
    [InlineData("undo-twice", false)]
    [InlineData("jump", false)]
    [InlineData("clear", false)]
    [InlineData("redo", true)]
    public async Task DevelopChangesAreSkippedAndRedoBackIsEligible(string action, bool restored)
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        var target = photos[1];
        var before = new EditSettings { Exposure = .25 };
        target.EditSettings = before;
        await fixture.Catalog.SaveEditSettingsWithHistoryAsync(target.CatalogId, before, null, before: new());
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        vm.SelectedImage = target;
        vm.IsDevelopMode = true;
        await vm.PendingHistoryLoadTask!;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.InitialPreviewActivityCount == 0);

        switch (action)
        {
            case "edit":
                vm.Exposure = 2;
                fixture.AdvancePreviewClock();
                await vm.PendingPreviewDebounceTask!;
                break;

            case "undo":
            case "undo-twice":
            case "redo":
                await vm.UndoCommand.ExecuteAsync(null);

                if (action == "undo-twice") await vm.UndoCommand.ExecuteAsync(null);
                if (action == "redo") await vm.RedoCommand.ExecuteAsync(null);

                break;

            case "jump":
                await vm.JumpToHistoryStepCommand.ExecuteAsync(vm.HistoryEntries.Last());
                break;

            case "clear":
                await vm.ClearHistoryCommand.ExecuteAsync(null);
                break;
        }

        var changed = EditSettingsJson.Serialize(target.EditSettings);
        vm.IsDevelopMode = false;
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(restored ? "Restored 11 photos" : "Restored 10 of 11 · 1 changed since", vm.TransientStatus);
        Assert.Equal(restored ? EditSettingsJson.Serialize(before) : changed, EditSettingsJson.Serialize(target.EditSettings));
        Assert.All(photos.Skip(2), image => Assert.Equal(0, image.EditSettings.Exposure));
        var persisted = await fixture.Catalog.LoadImageStatesAsync([target.FilePath]);
        Assert.Equal(EditSettingsJson.Serialize(target.EditSettings),
            EditSettingsJson.Serialize(Assert.Single(persisted[target.FilePath]).EditSettings));
    }

    [AvaloniaFact]
    public async Task DevelopPasteFolderChangeAndRestartClearOffer()
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        vm.CopyEditSettingsCommand.Execute(null);
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        vm.SelectedImage = photos[1];
        vm.IsDevelopMode = true;
        await vm.PendingHistoryLoadTask!;
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        vm.IsDevelopMode = false;
        Assert.False(vm.IsBatchUndoOffered);
        photos[0].EditSettings.Exposure = 2;
        vm.SelectedImage = photos[0];
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        Assert.True(vm.IsBatchUndoOffered);
        await vm.LoadFolderAsync(fixture.Catalog.CatalogPath);
        Assert.False(vm.IsBatchUndoOffered);
        await using var restarted = new MainWindowViewModel(fixture.Catalog);
        Assert.False(restarted.IsBatchUndoOffered);
    }

    private static async Task<ImageFile[]> PrepareAsync(SyncTransferParityVm fixture)
    {
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new() { Exposure = 1 });
        var photos = new List<ImageFile> { source };

        for (var index = 0; index < 11; index++)
        {
            photos.Add(await fixture.ImageAsync($"target-{index}", new()));
        }

        var vm = fixture.Vm;
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = false;
        vm.Browse.SetImages(photos);
        vm.SelectedImage = source;
        vm.SelectAllCommand.Execute(null);
        vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);

        return photos.ToArray();
    }
}

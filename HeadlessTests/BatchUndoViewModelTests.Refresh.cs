using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BatchUndoViewModelTests
{
    [AvaloniaFact]
    public async Task DevelopHistoryRefreshDoesNotWaitOnItsOwnUndo()
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        await vm.SyncSettingsCommand.ExecuteAsync(null);
        var release = Signal();
        fixture.Catalog.EditHistoryWriteGateAsync = () => release.Task;
        var undo = vm.UndoBatchCommand.ExecuteAsync(null);

        try
        {
            vm.SelectedImage = photos[1];
            vm.IsDevelopMode = true;
            Assert.False(vm.PendingHistoryLoadTask!.IsCompleted);
            release.SetResult();
            await undo.WaitAsync(TestWaits.Condition);
            await vm.PendingHistoryLoadTask.WaitAsync(TestWaits.Condition);
            Assert.True(vm.IsHistoryLoaded);
            Assert.Empty(vm.HistoryEntries);
            Assert.Equal(0, vm.Exposure);
        }
        finally
        {
            release.TrySetResult();
            await undo;
        }
    }

    [AvaloniaFact]
    public async Task FolderReloadWaitsForUndoBeforeReplacingModels()
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;

        foreach (var photo in photos)
        {
            await File.WriteAllBytesAsync(photo.FilePath, []);
        }

        await vm.SyncSettingsCommand.ExecuteAsync(null);
        var release = Signal();
        fixture.Catalog.EditHistoryWriteGateAsync = () => release.Task;
        var undo = vm.UndoBatchCommand.ExecuteAsync(null);
        Task? reload = null;

        try
        {
            var saves = (Dictionary<string, Task>)typeof(MainWindowViewModel)
                .GetField("_imageHistorySaves", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;

            foreach (var target in photos.Skip(1))
            {
                Assert.True(saves.TryGetValue(target.FilePath, out var pending));
                Assert.False(pending.IsCompleted);
            }

            reload = vm.LoadFolderAsync(Path.GetDirectoryName(photos[1].FilePath)!);
            Assert.False(reload.IsCompleted);
            release.SetResult();
            await Task.WhenAll(undo, reload).WaitAsync(TestWaits.Condition);

            foreach (var target in photos.Skip(1))
            {
                var replacement = Assert.Single(vm.Browse.VisibleImages, image => image.CatalogId == target.CatalogId);
                Assert.NotSame(target, replacement);
                Assert.Equal(0, replacement.EditSettings.Exposure);
                var stored = await fixture.Catalog.LoadImageStatesAsync([target.FilePath]);
                Assert.Equal(0, Assert.Single(stored[target.FilePath]).EditSettings.Exposure);
                Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
            }
        }
        finally
        {
            release.TrySetResult();
            await undo;
            if (reload != null) await reload;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoReloadsDisplayedLoupeWithRestoredPixelsAndGeometry(bool paste)
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        var target = photos[1];
        target.EditSettings.Crop = new() { Left = 0, Top = 0, Right = .5, Bottom = 1 };
        await fixture.Catalog.SaveEditSettingsAsync(target.CatalogId, target.EditSettings);
        vm.SelectedImage = target;
        vm.EnterLoupeCommand.Execute(null);
        await vm.LoupeLoadingTask.WaitAsync(TestWaits.Condition);
        var originalSize = vm.LoupePane!.OriginalViewPixelSize;
        var originalLuma = BitmapConversionService.EstimateMeanLuma(vm.LoupePane.Preview!);
        vm.ExitLoupeCommand.Execute(null);
        vm.SelectedImage = photos[0];
        vm.ShowPasteSettingsAsync = dialog =>
        {
            foreach (var group in dialog.Groups) group.IsSelected = true;

            return Task.FromResult(true);
        };

        if (paste)
        {
            vm.CopyEditSettingsCommand.Execute(null);
            vm.Browse.ToggleSelection(photos[0]);
            await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        }
        else
        {
            await vm.SyncSettingsCommand.ExecuteAsync(null);
        }

        vm.SelectedImage = target;
        vm.EnterLoupeCommand.Execute(null);
        await vm.LoupeLoadingTask.WaitAsync(TestWaits.Condition);
        var syncedPane = vm.LoupePane!;
        Assert.NotEqual(originalSize, syncedPane.OriginalViewPixelSize);
        Assert.NotEqual(originalLuma, BitmapConversionService.EstimateMeanLuma(syncedPane.Preview!));
        vm.PublishLoupeRequiredDeviceLongEdge(32, true);
        await vm.LoupeLoadingTask.WaitAsync(TestWaits.Condition);
        await vm.UndoBatchCommand.ExecuteAsync(null);
        await vm.LoupeLoadingTask.WaitAsync(TestWaits.Condition);
        var restoredPane = vm.LoupePane!;
        Assert.NotSame(syncedPane, restoredPane);
        Assert.Equal(originalSize, restoredPane.OriginalViewPixelSize);
        Assert.Equal(originalLuma, BitmapConversionService.EstimateMeanLuma(restoredPane.Preview!), 3);
        Assert.Same(restoredPane.Preview, restoredPane.PreviewResolutionBitmap);
        Assert.False(restoredPane.IsRefinementQueued);
        Assert.Equal(new PixelSize(8, 12), restoredPane.OriginalViewPixelSize);
    }
}

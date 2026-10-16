using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BatchUndoViewModelTests
{
    public static IEnumerable<object[]> UndoCacheInterleavingCases =>
        Enumerable.Range(1, 10).SelectMany(iteration => new[]
        {
            new object[] { iteration, false, false },
            new object[] { iteration, false, true },
            new object[] { iteration, true, false },
            new object[] { iteration, true, true }
        });

    [AvaloniaTheory]
    [MemberData(nameof(UndoCacheInterleavingCases))]
    public async Task UndoReadsConsistentCacheGeneration(int iteration, bool paste, bool replaceAfterMetadata)
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
        var originalSettings = target.EditSettings.Clone();
        using var originalPixels = BitmapConversionService.ConvertToMagickImage(vm.LoupePane.Preview!);
        var cache = (PreviewCacheService)typeof(PreviewService)
            .GetField("_previewCache", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(vm.ImageService.Previews)!;
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
        using var editedPixels = BitmapConversionService.ConvertToMagickImage(syncedPane.Preview!);
        var editedSettings = target.EditSettings.Clone();
        var editedSize = syncedPane.OriginalViewPixelSize;
        vm.PublishLoupeRequiredDeviceLongEdge(32, true);
        await vm.LoupeLoadingTask.WaitAsync(TestWaits.Condition);
        await TestWaits.UntilAsync(() => vm.ImageService.Previews.PendingCacheWrites == 0);
        Assert.True(await cache.QueueSaveToCache(target, originalPixels!,
            RenderSettingsHash.Compute(originalSettings),
            new PreviewCacheIdentity(originalSize, new PixelSize(16, 12)))
            .WaitAsync(TestWaits.Condition));
        bool? replacementPersisted = null;

        if (replaceAfterMetadata)
        {
            cache.AfterMetadataReadForTesting = () =>
            {
                cache.AfterMetadataReadForTesting = null;
                replacementPersisted = cache.QueueSaveToCache(target, editedPixels!,
                    RenderSettingsHash.Compute(editedSettings),
                    new PreviewCacheIdentity(editedSize, new PixelSize(16, 12)))
                    .WaitAsync(TestWaits.Condition).GetAwaiter().GetResult();
            };
        }

        await vm.UndoBatchCommand.ExecuteAsync(null);
        if (replaceAfterMetadata) Assert.True(replacementPersisted, $"iteration={iteration} paste={paste}");
        Assert.Equal(0, target.EditSettings.Exposure);
        var persisted = await fixture.Catalog.LoadImageStatesAsync([target.FilePath]);
        Assert.Equal(0, Assert.Single(persisted[target.FilePath]).EditSettings.Exposure);
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

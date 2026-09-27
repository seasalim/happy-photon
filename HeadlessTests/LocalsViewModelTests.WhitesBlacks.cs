using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhitesBlacksCommitUndoRedoResetAndReload(bool mono)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock, raw: mono, mono: mono);
        await Prepare(vm, catalog);
        vm.OnSliderEditStarted(); vm.Whites = 50; vm.Blacks = -50; vm.OnSliderEditCompleted();
        clock.Advance(TimeSpan.FromMilliseconds(200)); await vm.PendingPreviewDebounceTask!;
        Assert.True(vm.CanReset);
        Assert.Equal(50, vm.SelectedImage!.EditSettings.Whites);
        await vm.UndoCommand.ExecuteAsync(null); Assert.Equal(0, vm.Whites);
        await vm.RedoCommand.ExecuteAsync(null); Assert.Equal(-50, vm.Blacks);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        vm.OnSliderEditStarted(); vm.LocalWhites = 30; vm.LocalBlacks = -20; vm.OnSliderEditCompleted();
        clock.Advance(TimeSpan.FromMilliseconds(200)); await vm.PendingPreviewDebounceTask!;
        var state = vm.SelectedImage.EditSettings.Clone();
        await vm.UndoCommand.ExecuteAsync(null); Assert.Equal(0, vm.LocalWhites);
        await vm.RedoCommand.ExecuteAsync(null); Assert.Equal(-20, vm.LocalBlacks);
        await vm.ResetLocalAdjustmentsCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.LocalWhites); Assert.Equal(0, vm.LocalBlacks);
        await vm.UndoCommand.ExecuteAsync(null); Assert.Equal(30, vm.LocalWhites);
        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.Whites); Assert.Equal(0, vm.Blacks); Assert.Empty(vm.Locals);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.True(state.HasSameEdits(vm.SelectedImage.EditSettings));
        await using var reloaded = CreateVm(catalog, raw: mono, mono: mono);
        var path = vm.SelectedImage.FilePath;
        var saved = Assert.Single((await catalog.LoadImageStatesAsync([path]))[path]);
        var image = new ImageFile(path) { CatalogId = saved.CatalogId, EditSettings = saved.EditSettings };
        reloaded.Browse.SetImages([image]); reloaded.SelectedImage = image;
        await TestWaits.UntilAsync(() => reloaded.IsHistoryLoaded && reloaded.PreviewImage != null);
        Assert.Equal(50, reloaded.Whites); Assert.Equal(-50, reloaded.Blacks);
        Assert.Equal(30, Assert.Single(reloaded.Locals).Whites);
        Assert.Equal(-20, Assert.Single(reloaded.Locals).Blacks);
    }
}

using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresenceCommitUndoRedoResetAndReload(bool mono)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock, raw: mono, mono: mono);
        await Prepare(vm, catalog);

        vm.OnSliderEditStarted();
        vm.Texture = 50;
        vm.Clarity = -50;
        vm.OnSliderEditCompleted();
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.True(vm.CanReset);
        Assert.Equal(50, vm.SelectedImage!.EditSettings.Texture);
        Assert.Equal(-50, vm.SelectedImage.EditSettings.Clarity);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.Texture);
        Assert.Equal(0, vm.Clarity);

        await vm.RedoCommand.ExecuteAsync(null);
        Assert.Equal(50, vm.Texture);
        Assert.Equal(-50, vm.Clarity);

        var state = vm.SelectedImage.EditSettings.Clone();
        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.Texture);
        Assert.Equal(0, vm.Clarity);
        Assert.Equal(0, vm.SelectedImage.EditSettings.Texture);
        Assert.Equal(0, vm.SelectedImage.EditSettings.Clarity);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.True(state.HasSameEdits(vm.SelectedImage.EditSettings));

        await using var reloaded = CreateVm(catalog, raw: mono, mono: mono);
        var path = vm.SelectedImage.FilePath;
        var saved = Assert.Single((await catalog.LoadImageStatesAsync([path]))[path]);
        var image = new ImageFile(path) { CatalogId = saved.CatalogId, EditSettings = saved.EditSettings };
        reloaded.Browse.SetImages([image]);
        reloaded.SelectedImage = image;
        await TestWaits.UntilAsync(() => reloaded.IsHistoryLoaded && reloaded.PreviewImage != null);
        Assert.Equal(50, reloaded.Texture);
        Assert.Equal(-50, reloaded.Clarity);

        reloaded.SelectedImage = null;
        Assert.Equal(0, reloaded.Texture);
        Assert.Equal(0, reloaded.Clarity);
    }
}

using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DetailViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("detail-vm");

    [Fact]
    public async Task DetailEdits_CanonicalizePersistResetAndUndo()
    {
        using var catalog = await _fx.CreateCatalogAsync("catalog");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("photo.dng"));
        vm.SelectedImage = image;

        Assert.Equal(25, vm.CaptureSharpen);
        Assert.Null(image.EditSettings.Detail.CaptureSharpen);

        vm.CaptureSharpen = 44;
        vm.LuminanceNr = 68;
        vm.ChromaNr = 57;
        await TestWaits.UntilAsync(() =>
            image.EditSettings.Detail.CaptureSharpen == 44 &&
            image.EditSettings.Detail.LuminanceNr == 68 &&
            image.EditSettings.Detail.ChromaNr == 57);

        Assert.True(vm.CanReset);
        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Null(image.EditSettings.Detail.CaptureSharpen);
        Assert.Equal(0, image.EditSettings.Detail.LuminanceNr);
        Assert.Equal(0, image.EditSettings.Detail.ChromaNr);
        Assert.Equal(25, vm.CaptureSharpen);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(44, image.EditSettings.Detail.CaptureSharpen);
        Assert.Equal(68, image.EditSettings.Detail.LuminanceNr);
        Assert.Equal(57, image.EditSettings.Detail.ChromaNr);

        vm.CaptureSharpen = 25;
        await TestWaits.UntilAsync(() =>
            image.EditSettings.Detail.CaptureSharpen == null);
        Assert.True(image.EditSettings.HasEdits);
    }

    [Fact]
    public async Task RenderReconcile_KeepsUnsavedSharpenWhenCapabilityIsUnchanged()
    {
        using var catalog = _fx.CreateCatalog("sticky");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("photo.dng"));
        vm.SelectedImage = image;
        Assert.Equal(25, vm.CaptureSharpen);

        // A render completes while the debounced save is still pending: the
        // persisted value is null, but the slider must keep the user's value.
        vm.CaptureSharpen = 40;
        vm.ReconcileHighlightReconstructionCapability(image, isRawSource: true);

        Assert.Equal(40, vm.CaptureSharpen);
    }

    [Fact]
    public async Task SourceDefault_ReconcilesWithoutCreatingAnEdit()
    {
        using var catalog = _fx.CreateCatalog("reconcile");
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(_fx.Path("guard.dng"));
        vm.SelectedImage = image;

        Assert.Equal(25, vm.CaptureSharpen);
        vm.ReconcileHighlightReconstructionCapability(image, isRawSource: false);

        Assert.Equal(0, vm.CaptureSharpen);
        Assert.Null(image.EditSettings.Detail.CaptureSharpen);
        Assert.Equal(0, vm.LuminanceNr);
    }

    private MainWindowViewModel CreateViewModel(CatalogService catalog)
    {
        var vm = _fx.CreateViewModel(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        return vm;
    }

    public void Dispose() => _fx.Dispose();
}

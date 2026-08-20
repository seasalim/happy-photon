using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DetailViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-detail-vm-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task DetailEdits_CanonicalizePersistResetAndUndo()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "catalog"));
        await catalog.InitializeAsync();
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root, "photo.dng"));
        vm.SelectedImage = image;

        Assert.Equal(25, vm.CaptureSharpen);
        Assert.Null(image.EditSettings.Detail.CaptureSharpen);

        vm.CaptureSharpen = 44;
        vm.NoiseReduction = FbddMode.Full;
        vm.ChromaNr = 57;
        await TestWaits.UntilAsync(() =>
            image.EditSettings.Detail.CaptureSharpen == 44 &&
            image.EditSettings.Detail.NoiseReduction == FbddMode.Full &&
            image.EditSettings.Detail.ChromaNr == 57);

        Assert.True(vm.CanReset);
        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Null(image.EditSettings.Detail.CaptureSharpen);
        Assert.Equal(FbddMode.Off, image.EditSettings.Detail.NoiseReduction);
        Assert.Equal(0, image.EditSettings.Detail.ChromaNr);
        Assert.Equal(25, vm.CaptureSharpen);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(44, image.EditSettings.Detail.CaptureSharpen);
        Assert.Equal(FbddMode.Full, image.EditSettings.Detail.NoiseReduction);
        Assert.Equal(57, image.EditSettings.Detail.ChromaNr);

        vm.CaptureSharpen = 25;
        await TestWaits.UntilAsync(() =>
            image.EditSettings.Detail.CaptureSharpen == null);
        Assert.True(image.EditSettings.HasEdits);
    }

    [Fact]
    public async Task RenderReconcile_KeepsUnsavedSharpenWhenCapabilityIsUnchanged()
    {
        using var catalog = new CatalogService(Path.Combine(_root, "sticky"));
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root, "photo.dng"));
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
        using var catalog = new CatalogService(Path.Combine(_root, "reconcile"));
        await using var vm = CreateViewModel(catalog);
        var image = new ImageFile(Path.Combine(_root, "guard.dng"));
        vm.SelectedImage = image;

        Assert.Equal(25, vm.CaptureSharpen);
        vm.ReconcileHighlightReconstructionCapability(image, isRawSource: false);

        Assert.Equal(0, vm.CaptureSharpen);
        Assert.Null(image.EditSettings.Detail.CaptureSharpen);
        Assert.False(vm.IsNoiseReductionEnabled);
    }

    private static MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        new(
            catalog,
            new NullBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally))
        {
            IsDevelopMode = true
        };

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class NullBaseLoader : IBaseImageLoader
    {
        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            BaseImageLoadOutcome.FromImage(
                null,
                BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) => null;
    }
}

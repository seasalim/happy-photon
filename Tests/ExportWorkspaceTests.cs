using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportWorkspaceTests : IDisposable
{
    private readonly CatalogVmFixture _fx = new("export-workspace");

    [Fact]
    public async Task ModeShimsAndEscape_PreserveTheWorkspaceOfOrigin()
    {
        await using var vm = CreateViewModel(new NullBaseLoader());
        var exportNotifications = new List<bool>();
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.IsExportMode))
                exportNotifications.Add(vm.IsExportMode);
        };

        vm.SwitchToExportCommand.Execute(null);

        Assert.Equal(WorkspaceMode.Export, vm.WorkspaceMode);
        Assert.True(vm.IsExportMode);
        Assert.False(vm.IsBrowseMode);
        Assert.False(vm.IsDevelopMode);
        vm.HandleEscapeCommand.Execute(null);
        Assert.Equal(WorkspaceMode.Browse, vm.WorkspaceMode);

        vm.SwitchToDevelopCommand.Execute(null);
        vm.SwitchToExportCommand.Execute(null);
        vm.HandleEscapeCommand.Execute(null);

        Assert.Equal(WorkspaceMode.Develop, vm.WorkspaceMode);
        Assert.Equal([true, false, false, true, false], exportNotifications);
    }

    [Fact]
    public async Task ExportActionsAndIncludeLayer_LeaveBrowseSelectionUntouched()
    {
        using var catalog = await _fx.CreateCatalogAsync("actions");
        await using var vm = CreateViewModel(new NullBaseLoader(), catalog);
        var first = await CreateImageAsync(catalog, "first.jpg", exposure: 2);
        var second = await CreateImageAsync(catalog, "second.jpg");
        vm.Browse.SetImages([first, second]);
        vm.SelectedImage = first;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.Browse.ToggleSelection(first);
        vm.Browse.ToggleSelection(second);
        vm.RefreshSelectedCount();
        var deleteConfirmations = 0;
        vm.ConfirmDeleteAsync = _ =>
        {
            deleteConfirmations++;
            return Task.FromResult(true);
        };

        vm.SwitchToExportCommand.Execute(null);
        Assert.Equal("2 captures × 1 recipe → 2 files", vm.ExportCountLine);
        vm.ExportCaptures[0].IsIncluded = false;
        Assert.Equal(1, vm.IncludedExportCaptureCount);
        Assert.Equal(2, vm.SelectedCount);
        Assert.Equal("1 capture × 1 recipe → 1 file", vm.ExportCountLine);

        WorkspaceKeyRouting.TryHandleSpace(vm, focusedElement: null);
        vm.SelectNextImageCommand.Execute(null);
        vm.SelectAllCommand.Execute(null);
        vm.DeselectAllCommand.Execute(null);
        await vm.TogglePickedImageCommand.ExecuteAsync(null);
        await vm.SetRatingCommand.ExecuteAsync(5);
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        await vm.DeleteImageCommand.ExecuteAsync(null);

        Assert.Equal([first, second], vm.Browse.GetSelectedImages());
        Assert.Equal(ImageFlag.Unflagged, first.Flag);
        Assert.Equal(ImageFlag.Unflagged, second.Flag);
        Assert.Equal(0, first.Rating);
        Assert.Equal(0, second.Rating);
        Assert.Equal(0, second.EditSettings.Exposure);
        Assert.Equal(0, deleteConfirmations);

        vm.ExportSettings.ExportHiRes = false;
        Assert.Equal(0, vm.ArmedExportRecipeCount);
        Assert.Equal(0, vm.ExportFileCount);
        Assert.Equal("1 capture × 0 recipes → 0 files", vm.ExportCountLine);
    }

    [Fact]
    public async Task EnteringExport_InitializesAutomaticDestination()
    {
        await using var vm = CreateViewModel(new NullBaseLoader());
        vm.CurrentFolderPath = _fx.Root;

        vm.SwitchToExportCommand.Execute(null);

        Assert.Equal(Path.Combine(_fx.Root, "export"),
            vm.ExportSettings.OutputFolder);
    }

    [Fact]
    public async Task Enter_AppliesCropInCropMode()
    {
        using var catalog = await _fx.CreateCatalogAsync("enter-crop");
        await using var vm = CreateViewModel(new NullBaseLoader(), catalog);
        var image = await CreateImageAsync(catalog, "crop.jpg");
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        vm.SwitchToDevelopCommand.Execute(null);
        vm.ToggleCropModeCommand.Execute(null);
        vm.CurrentCrop = new CropRegion
        {
            Left = 0.1,
            Top = 0.1,
            Right = 0.9,
            Bottom = 0.9
        };

        await vm.HandleEnterCommand.ExecuteAsync(null);

        Assert.False(vm.IsCropMode);
        Assert.Equal(0.1, image.EditSettings.Crop?.Left);
        Assert.True(vm.IsDevelopMode);
    }

    [Fact]
    public async Task ThumbnailEntry_SwitchesBrowseToDevelop()
    {
        await using var vm = CreateViewModel(new NullBaseLoader());
        var image = new ImageFile(_fx.Path("develop.jpg"));
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;

        vm.EnterDevelopModeCommand.Execute(null);

        Assert.True(vm.IsDevelopMode);
        Assert.Null(vm.ExportReport);
    }

    [Fact]
    public async Task ThumbnailEntry_LeavesDevelopActive()
    {
        await using var vm = CreateViewModel(new NullBaseLoader());
        var image = new ImageFile(_fx.Path("browse.jpg"));
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        vm.SwitchToDevelopCommand.Execute(null);

        vm.EnterDevelopModeCommand.Execute(null);

        Assert.True(vm.IsDevelopMode);
        Assert.Null(vm.ExportReport);
    }

    [Fact]
    public async Task Enter_RunsExportOnlyInExportMode()
    {
        await using var vm = CreateViewModel(new NullBaseLoader());
        var image = new ImageFile(_fx.Path("export.jpg"));
        vm.Browse.SetImages([image]);
        vm.Browse.ToggleSelection(image);
        vm.SelectedImage = image;
        vm.RefreshSelectedCount();
        vm.SwitchToExportCommand.Execute(null);
        vm.ExportSettings.ExportHiRes = false;

        await vm.HandleEnterCommand.ExecuteAsync(null);

        Assert.True(vm.IsExportMode);
        Assert.Equal("Nothing to export", vm.ExportReport?.Heading);
    }

    private MainWindowViewModel CreateViewModel(
        IBaseImageLoader loader,
        CatalogService? catalog = null) =>
        _fx.CreateViewModel(
            catalog ?? _fx.CreateCatalog(),
            loader,
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            postSelection: action => action());

    private async Task<ImageFile> CreateImageAsync(
        CatalogService catalog,
        string name,
        double exposure = 0)
    {
        var image = new ImageFile(_fx.Path(name))
        {
            EditSettings = new EditSettings { Exposure = exposure }
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        return image;
    }

    public void Dispose() => _fx.Dispose();
}

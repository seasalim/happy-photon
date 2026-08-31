using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportWorkspaceCropTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("export-workspace-crop");

    [AvaloniaFact]
    public async Task Enter_AppliesCropInCropMode()
    {
        using var catalog = await _fixture.CreateCatalogAsync("enter-crop");
        await using var vm = CreateViewModel(catalog);
        var image = await CreateImageAsync(catalog, "crop.jpg");
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;
        vm.SwitchToDevelopCommand.Execute(null);
        await TestWaits.UntilAsync(() =>
            vm.IsHistoryLoaded && vm.PreviewImage != null);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
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

    private MainWindowViewModel CreateViewModel(CatalogService catalog) =>
        _fixture.CreateViewModel(
            catalog,
            new CountingPairLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(
                SourceAvailability.AvailableLocally),
            postSelection: action => action());

    private async Task<ImageFile> CreateImageAsync(
        CatalogService catalog,
        string name)
    {
        var image = new ImageFile(_fixture.Path(name));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        return image;
    }

    public void Dispose() => _fixture.Dispose();
}

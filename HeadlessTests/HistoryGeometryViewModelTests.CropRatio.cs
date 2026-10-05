using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class HistoryGeometryViewModelTests
{
    [AvaloniaFact]
    public async Task RatioChoicesAndSwapsRemainDraftUntilOneApplyStep()
    {
        using var catalog = await _fixture.CreateCatalogAsync("ratio-history");
        await using var vm = CreateViewModel(catalog);
        var image = await CreateImageAsync(catalog, "ratio-history.jpg");
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        Assert.Equal("Original", vm.CropRatio);
        vm.ChooseCropRatio("3:2");
        vm.SwapCropRatioCommand.Execute(null);
        Assert.Empty(vm.HistoryEntries);
        Assert.Null(image.EditSettings.Crop);

        await vm.ApplyCropCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Crop", "Original" }, vm.HistoryEntries.Select(entry => entry.Label));
        Assert.Equal(2d / 3, CropGeometry.DraftPixelRatio(image.EditSettings.Crop!, 4d / 3), 12);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        vm.ChooseCropRatio("1:1");
        await vm.CancelCropCommand.ExecuteAsync(null);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        Assert.Equal("3:2", vm.CropRatio);
        Assert.Equal(2, vm.HistoryEntries.Count);
    }
}

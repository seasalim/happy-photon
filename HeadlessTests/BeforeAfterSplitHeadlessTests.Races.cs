using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BeforeAfterSplitHeadlessTests
{
    [AvaloniaFact]
    public async Task SmallerSameSettingsRequestDoesNotSupersedeSourceCappedRefinement()
    {
        using var catalog = await _fx.CreateCatalogAsync("source-capped-refinement");
        await using var vm = _fx.CreateViewModel(
            catalog,
            new GrayLoader(),
            _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        vm.SelectedImage = new ImageFile(_fx.Path("source-capped-refinement.jpg"));
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);

        await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
        await TestWaits.UntilAsync(() => vm.BeforeAfterPreviewImage != null);
        vm.PublishBeforeAfterRequiredDeviceLongEdge(3200);
        await TestWaits.UntilAsync(() =>
            vm.BeforeAfterPreviewImage is { } bitmap &&
            Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height) == 1280);
        var refined = vm.BeforeAfterPreviewImage;
        var renderSerial = SideSurfaceRenderSerial(vm);

        vm.PublishBeforeAfterRequiredDeviceLongEdge(1600);

        Assert.Equal(renderSerial, SideSurfaceRenderSerial(vm));
        Assert.Same(refined, vm.BeforeAfterPreviewImage);
    }

    private static long SideSurfaceRenderSerial(MainWindowViewModel vm) =>
        (long)typeof(PreviewService).GetField(
            "_sideSurfaceSerial",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic)!
            .GetValue(vm.ImageService.Previews)!;
}

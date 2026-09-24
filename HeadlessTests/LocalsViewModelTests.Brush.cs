using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task LoadedBrushCannotUseGradientHandlesOrGesture()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        vm.SelectedLocal!.Type = "brush";
        vm.SelectedLocal.Strokes = [new() { Points = [new(8192, 8192)] }];
        var before = vm.SelectedLocal with { };
        var overlay = new LocalsOverlayControl();
        overlay.Measure(new Size(400, 300)); overlay.Arrange(new Rect(0, 0, 400, 300));
        Assert.Null(overlay.HitHandle(new Point(200, 150), vm.SelectedLocal, new LocalsFrame(400, 300, 0, 0, 1, 1)));
        foreach (var handle in Enum.GetValues<LocalHandle>().Where(h => h != LocalHandle.Create))
            Assert.False(vm.BeginLocalsGesture(handle, new Point(.5, .5)));
        Assert.False(vm.IsLocalsGestureActive);
        Assert.Equal(before, vm.SelectedLocal);
    }
}

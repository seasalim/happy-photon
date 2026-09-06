using Avalonia.Headless.XUnit;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData("Temperature", 50)]
    [InlineData("Tint", 50)]
    [InlineData("Saturation", 100)]
    public async Task ColorSliderReleaseIsOneUndoableStep(string field, double maximum)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var property = typeof(MainWindowViewModel).GetProperty("Local" + field)!;
        var count = vm.HistoryEntries.Count;
        vm.OnSliderEditStarted();
        property.SetValue(vm, maximum / 2);
        property.SetValue(vm, maximum + 20);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(count, vm.HistoryEntries.Count);
        Assert.Equal(maximum, property.GetValue(vm));
        vm.OnSliderEditCompleted();
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0d, property.GetValue(vm));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetColorPreservesGeometryEnablementAndMonoDormancy(bool mono)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, raw: mono, mono: mono);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddRadialCommand.Execute(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var local = vm.SelectedLocal!;
        local.Temperature = 30; local.Tint = -20; local.Saturation = 40; local.Exposure = -1;
        local.Outside = true;
        await vm.ToggleLocalEnabledCommand.ExecuteAsync(local);
        var before = vm.SelectedLocal! with { };
        Assert.Equal(!mono, vm.CanEditLocalColor);
        if (mono)
        {
            vm.LocalTemperature = 0; vm.LocalTint = 0; vm.LocalSaturation = 0;
            Assert.Equal(before, vm.SelectedLocal);
        }
        var count = vm.HistoryEntries.Count;
        await vm.ResetLocalAdjustmentsCommand.ExecuteAsync(null);
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        Assert.Equal("Reset adjustments", vm.HistoryEntries[0].Label);
        Assert.Equal(before with { Exposure = 0, Temperature = 0, Tint = 0, Saturation = 0 }, vm.SelectedLocal);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(before, vm.SelectedLocal);
        await vm.ResetEditsCommand.ExecuteAsync(null);
        Assert.Empty(vm.Locals);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(before, vm.SelectedLocal);
    }
}

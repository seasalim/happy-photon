using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HappyPhoton.Views;
using HappyPhoton.Models;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LuminanceToggleSliderResetAndPresetPreserveOwnership(bool mono)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock, mono, mono);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var count = vm.HistoryEntries.Count;
        await vm.ToggleLocalLuminanceCommand.ExecuteAsync(null);
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        Assert.Equal("Luminance Range", vm.HistoryEntries[0].Label);
        Assert.True(vm.CanEditLocalLuminance);
        Assert.Equal(new LuminanceRange { Enabled = true }, vm.SelectedLocal!.Luminance);
        vm.OnSliderEditStarted(); vm.LocalLuminanceLower = 20; vm.LocalLuminanceLower = 47;
        clock.Advance(TimeSpan.FromMilliseconds(200)); await vm.PendingPreviewDebounceTask!;
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        vm.OnSliderEditCompleted("Luminance Range");
        clock.Advance(TimeSpan.FromMilliseconds(200)); await vm.PendingPreviewDebounceTask!;
        Assert.Equal(count + 2, vm.HistoryEntries.Count);
        await vm.UndoCommand.ExecuteAsync(null); Assert.Equal(0, vm.LocalLuminanceLower);
        await vm.RedoCommand.ExecuteAsync(null); Assert.Equal(47, vm.LocalLuminanceLower);
        await vm.ToggleLocalLuminanceCommand.ExecuteAsync(null);
        Assert.False(vm.IsLocalLuminanceEnabled); Assert.Equal(47, vm.LocalLuminanceLower);
        await vm.UndoCommand.ExecuteAsync(null);
        var range = vm.SelectedLocal!.Luminance;
        vm.SelectedLocal.Exposure = 1;
        await vm.ResetLocalAdjustmentsCommand.ExecuteAsync(null); Assert.Equal(range, vm.SelectedLocal!.Luminance);
        await vm.PresetService.UseDirectoryAsync(_fixture.Path("range-presets"));
        var preset = await vm.PresetService.SaveUserPresetAsync("Range", new() { Exposure = 1 });
        await vm.ApplyPresetAsync(preset.Id); Assert.Equal(range, vm.SelectedLocal!.Luminance);
        await vm.ResetEditsCommand.ExecuteAsync(null); Assert.Empty(vm.Locals);
        await vm.UndoCommand.ExecuteAsync(null); Assert.Equal(range, vm.SelectedLocal!.Luminance);
    }

    [AvaloniaFact]
    public async Task ClonePasteAndCancelledGeometryPreserveIndependentRangeValues()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        await vm.ToggleLocalLuminanceCommand.ExecuteAsync(null);
        vm.LocalLuminanceLower = 47;
        var image = vm.SelectedImage!;
        var expected = vm.SelectedLocal! with { };
        Assert.True(vm.BeginLocalsGesture(HappyPhoton.ViewModels.LocalHandle.Center, new(.5, .5)));
        vm.MoveLocalsGesture(new(.8, .8), 100); vm.EscapeLocals();
        Assert.Equal(expected, vm.SelectedLocal);
        vm.CopyEditSettingsCommand.Execute(null); await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(expected.Luminance, vm.SelectedLocal!.Luminance);
        await vm.NewVersionFromCurrentCommand.ExecuteAsync(null);
        Assert.NotSame(image, vm.SelectedImage);
        Assert.Equal(expected.Luminance, vm.SelectedLocal!.Luminance);
        vm.LocalLuminanceLower = 60;
        Assert.Equal(.47, image.EditSettings.Locals![0].Luminance!.Lower);
    }

    [AvaloniaFact]
    public async Task SupersededMaskCannotPublishAndDisabledNeutralLocalStillVisualizes()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        await vm.ToggleLocalLuminanceCommand.ExecuteAsync(null);
        vm.LocalLuminanceLower = 47;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.LocalMaskRenderGateAsync = () => { entered.TrySetResult(); return release.Task; };
        vm.ShowLocalMask = true;
        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            Assert.True(vm.IsLocalRangeMaskUpdating); Assert.Null(vm.LocalRangeMask);
            vm.ShowLocalMask = false;
        }
        finally { release.TrySetResult(); }
        await vm.PendingLocalMaskTask;
        Assert.Null(vm.LocalRangeMask);
        vm.LocalMaskRenderGateAsync = null;
        await vm.ToggleLocalEnabledCommand.ExecuteAsync(null);
        vm.ShowLocalMask = true; await vm.PendingLocalMaskTask;
        Assert.NotNull(vm.LocalRangeMask); Assert.False(vm.IsLocalRangeMaskUpdating);
        Assert.False(vm.SelectedLocal!.Enabled); Assert.Equal(0, vm.LocalExposure);
        vm.LocalLuminanceLower = 60;
        Assert.Null(vm.LocalRangeMask);
        await vm.PendingLocalMaskTask; Assert.NotNull(vm.LocalRangeMask);
        vm.CloseLocalsCommand.Execute(null); Assert.Null(vm.LocalRangeMask);
    }

    [AvaloniaFact]
    public async Task MaskReadsAllocateNothingAndOnlyWeightInputsReplaceTheMask()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        await vm.ToggleLocalLuminanceCommand.ExecuteAsync(null);
        vm.LocalLuminanceLower = 47;
        var renders = 0;
        vm.LocalMaskRenderGateAsync = () => { renders++; return Task.CompletedTask; };
        vm.ShowLocalMask = true; await vm.PendingLocalMaskTask;
        var mask = vm.LocalRangeMask;
        Assert.NotNull(mask);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) mask = vm.LocalRangeMask;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - allocated);
        var request = vm.PendingLocalMaskTask;
        vm.LocalExposure = 1; vm.LocalTemperature = 10; vm.LocalTint = 5; vm.LocalSaturation = 20;
        vm.Exposure = .5; vm.Contrast = 10; vm.Saturation = 15;
        Assert.Same(mask, vm.LocalRangeMask); Assert.Same(request, vm.PendingLocalMaskTask);
        Assert.False(vm.IsLocalRangeMaskUpdating); Assert.Equal(1, renders);
        clock.Advance(TimeSpan.FromMilliseconds(200)); await vm.PendingPreviewDebounceTask!;
        Assert.Same(mask, vm.LocalRangeMask); Assert.Equal(1, renders);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.LocalMaskRenderGateAsync = () => { renders++; return release.Task; };
        try
        {
            vm.LocalLuminanceLower = 60;
            Assert.Null(vm.LocalRangeMask); Assert.True(vm.IsLocalRangeMaskUpdating);
        }
        finally { release.TrySetResult(); }
        await vm.PendingLocalMaskTask;
        Assert.NotNull(vm.LocalRangeMask); Assert.NotSame(mask, vm.LocalRangeMask);
        Assert.Equal(2, renders);
        mask = vm.LocalRangeMask;
        vm.WhiteBalanceTint = 10;
        await vm.PendingLocalMaskTask;
        Assert.NotNull(vm.LocalRangeMask); Assert.NotSame(mask, vm.LocalRangeMask);
        Assert.Equal(3, renders);
    }

    [AvaloniaFact]
    public async Task FailedMaskClearsUpdatingAndRetriesOnlyAfterIdentityChanges()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        await vm.ToggleLocalLuminanceCommand.ExecuteAsync(null);
        vm.LocalLuminanceLower = 47;
        var renders = 0;
        vm.LocalMaskRenderGateAsync = () =>
        {
            renders++;
            return Task.FromException(new InvalidOperationException("Injected mask failure"));
        };
        vm.ShowLocalMask = true; await vm.PendingLocalMaskTask;
        Assert.Null(vm.LocalRangeMask); Assert.False(vm.IsLocalRangeMaskUpdating);
        var failed = vm.PendingLocalMaskTask;
        vm.LocalExposure = 1;
        Assert.Same(failed, vm.PendingLocalMaskTask);
        Assert.False(vm.IsLocalRangeMaskUpdating); Assert.Equal(1, renders);
        vm.LocalMaskRenderGateAsync = () => { renders++; return Task.CompletedTask; };
        vm.LocalLuminanceLower = 60; await vm.PendingLocalMaskTask;
        Assert.NotNull(vm.LocalRangeMask); Assert.False(vm.IsLocalRangeMaskUpdating);
        Assert.Equal(2, renders);
    }

    [AvaloniaFact]
    public async Task CollapsedLuminanceDisclosureKeepsEnableCheckboxVisibleAndKeyboardReachable()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var section = new LocalsEditSection { DataContext = vm };
        var window = new Window { Width = 260, Height = 700, Content = section };
        using var scope = new TestUiScope(window);
        var checkbox = section.FindControl<CheckBox>("LocalLuminanceEnabled")!;
        Assert.False(vm.IsLocalLuminanceExpanded);
        Assert.True(checkbox.IsEffectivelyVisible && checkbox.Bounds.Height > 0);
        Assert.True(checkbox.Focus());
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        await TestWaits.UntilAsync(() => vm.IsLocalLuminanceEnabled && !vm.ToggleLocalLuminanceCommand.IsRunning);
        Assert.True(checkbox.IsChecked);
        Assert.False(vm.IsLocalLuminanceExpanded);
    }
}

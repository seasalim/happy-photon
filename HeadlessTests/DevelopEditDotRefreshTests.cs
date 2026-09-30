using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopEditDotRefreshTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task D3FirstAndLastSliderEditRefreshAfterCommit(bool wheel)
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        await fixture.SelectAsync(await fixture.ImageAsync("sliders", new()));
        var vm = fixture.Vm;
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        Collapse(vm);
        vm.AdjustmentsGroup.IsExpanded = true;
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 350, Height = 1000, Content = panel };
        using var scope = new TestUiScope(window);
        var slider = panel.GetVisualDescendants().OfType<CompactSlider>()
            .Single(control => control.Label == "Exposure");
        slider.WheelTimeProvider = new TestTimeProvider();

        foreach (var adding in new[] { true, false })
        {
            vm.AdjustmentsGroup.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            if (wheel)
            {
                Assert.True(slider.IsEffectivelyEnabled);
                CompactSliderWheelTests.Wheel(slider, 0, adding ? 1 : -1);
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.LeftShift });
            }
            else
            {
                vm.OnSliderEditStarted();
                vm.Exposure = adding ? .5 : 0;
                vm.OnSliderEditCompleted("Exposure");
            }

            vm.AdjustmentsGroup.IsExpanded = false;
            fixture.AdvancePreviewClock();

            if (vm.PendingPreviewDebounceTask is { } preview)
            {
                await preview.WaitAsync(TestWaits.Condition);
            }

            if (vm.PendingHistoryCommitTask is { } commit)
            {
                await commit.WaitAsync(TestWaits.Condition);
            }

            Assert.Equal(adding, vm.SelectedImage!.EditSettings.Exposure != 0);
            AssertDots(vm, adding ? ["Adjustments"] : []);
        }
    }

    [AvaloniaFact]
    public async Task D3PresetPasteResetUndoRedoPhotoAndEmptySelection()
    {
        await using var fixture = new SyncTransferParityVm();
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        await fixture.InitializeAsync(availability: availability);
        var vm = fixture.Vm;
        var first = await fixture.ImageAsync("first", new());
        await fixture.SelectAsync(first);
        Collapse(vm);
        AssertDots(vm);
        var preset = await vm.PresetService.SaveUserPresetAsync("Dot test",
            new EditSettings { Texture = 12, Detail = new() { LuminanceNr = 10 } });
        await vm.ApplyPresetAsync(preset.Id);
        AssertDots(vm, "Presence", "Detail");
        await vm.UndoCommand.ExecuteAsync(null);
        AssertDots(vm);
        await vm.RedoCommand.ExecuteAsync(null);
        AssertDots(vm, "Presence", "Detail");
        vm.CopyEditSettingsCommand.Execute(null);
        await vm.ResetEditsCommand.ExecuteAsync(null);
        AssertDots(vm);
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        AssertDots(vm, "Presence", "Detail");
        await vm.UndoCommand.ExecuteAsync(null);
        AssertDots(vm);
        await vm.RedoCommand.ExecuteAsync(null);
        AssertDots(vm, "Presence", "Detail");

        var second = await fixture.ImageAsync("second", new EditSettings
        {
            Effects = new() { Grain = 10 },
            Lens = new() { ProfileOverride = "test lens" }
        });
        await fixture.SelectAsync(second);
        AssertDots(vm, "Effects", "Optics");
        await fixture.SelectAsync(first);
        AssertDots(vm, "Presence", "Detail");
        vm.SelectedImage = null;
        AssertDots(vm);
        Assert.All(vm.DevelopGroupList, group => Assert.False(group.HasEdits));

        var cloud = await fixture.ImageAsync("cloud", new EditSettings { Texture = 15 });
        availability.Availability = SourceAvailability.RequiresHydration;
        vm.SelectedImage = cloud;
        AssertDots(vm);
        Assert.All(vm.DevelopGroupList, group => Assert.False(group.HasEdits));
    }

    [AvaloniaFact]
    public async Task D3WhiteBalanceCurveAndDetailWritersRefreshCollapsedGroups()
    {
        await using var fixture = new SyncTransferParityVm();
        await fixture.InitializeAsync();
        var vm = fixture.Vm;
        await fixture.SelectAsync(await fixture.ImageAsync("writers", new()));
        Collapse(vm);
        vm.LuminanceNr = 10;
        fixture.AdvancePreviewClock();
        await vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
        AssertDots(vm, "Detail");
        vm.SelectedWhiteBalanceMode = "Daylight";
        fixture.AdvancePreviewClock();
        await vm.PendingPreviewDebounceTask!.WaitAsync(TestWaits.Condition);
        AssertDots(vm, "White Balance", "Detail");
        vm.OnCurveEditStarted();
        vm.CurrentCurve!.AddPointAndReturnIndex(.5, .6);
        await vm.OnCurveChangedAsync();
        AssertDots(vm, "White Balance", "Tone Curve", "Detail");
        await vm.ResetEditsCommand.ExecuteAsync(null);
        AssertDots(vm);
    }

    private static void Collapse(MainWindowViewModel vm) =>
        vm.RestoreDevelopGroups(vm.DevelopGroupList.ToDictionary(group => group.Name, _ => false));

    private static void AssertDots(MainWindowViewModel vm, params string[] names)
    {
        Assert.All(vm.DevelopGroupList, group => Assert.False(group.IsExpanded));
        Assert.Equal(names, vm.DevelopGroupList.Where(group => group.ShowsEditDot).Select(group => group.Name));
    }
}

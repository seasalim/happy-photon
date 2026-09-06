using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task ToolEntryPreservesSelectionAndSourceAvailabilityGates()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        vm.ShowWorkspaceReady(HappyPhoton.ViewModels.MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var panel = new DevelopEditPanel { DataContext = vm };
        using var scope = new TestUiScope(new Window { Width = 250, Height = 500, Content = panel });
        var crop = panel.FindControl<ToggleButton>("CropModeButton")!;
        var locals = panel.FindControl<ToggleButton>("LocalsModeButton")!;
        Assert.False(crop.IsEffectivelyEnabled);
        Assert.False(locals.IsEffectivelyEnabled);
        await Prepare(vm, catalog);
        Assert.True(crop.IsEffectivelyEnabled);
        Assert.True(locals.IsEffectivelyEnabled);
        vm.ApplyThumbnailLoadStatus(vm.SelectedImage!, ThumbnailLoadStatus.DeferredForHydration);
        Assert.False(crop.IsEffectivelyEnabled);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        Assert.True(vm.IsLocalsMode);
        Assert.False(vm.CanEditLocals);
        vm.CloseLocalsCommand.Execute(null);
        vm.ApplyThumbnailLoadStatus(vm.SelectedImage!, ThumbnailLoadStatus.Loaded);
        Assert.True(crop.IsEffectivelyEnabled);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.ApplyThumbnailLoadStatus(vm.SelectedImage!, ThumbnailLoadStatus.DeferredForHydration);
        Assert.True(panel.FindControl<Button>("CloseLocalsButton")!.IsEffectivelyEnabled);
        vm.CloseLocalsCommand.Execute(null);
        Assert.False(vm.IsLocalsMode);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ToolSwitchingRevealsSettingsAndRestoresCapabilities(bool mono)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, raw: mono, mono: mono);
        vm.ShowWorkspaceReady(HappyPhoton.ViewModels.MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await Prepare(vm, catalog);
        var panel = new DevelopEditPanel { DataContext = vm };
        var viewer = new DevelopViewerPane { DataContext = vm };
        using var scope = new TestUiScope(new Window { Width = 800, Height = 500,
            Content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,250"), Children = { viewer, panel } } });
        Grid.SetColumn(panel, 1);
        Dispatcher.UIThread.RunJobs();
        var scroll = panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!;
        var globals = panel.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Classes.Contains("global-edits"));
        var cropButton = panel.FindControl<ToggleButton>("CropModeButton")!;
        var localsButton = panel.FindControl<ToggleButton>("LocalsModeButton")!;
        Assert.Equal("", AutomationProperties.GetHelpText(cropButton));
        Assert.Equal("", AutomationProperties.GetHelpText(localsButton));
        Assert.False(cropButton.GetVisualDescendants().OfType<Ellipse>().Single().IsVisible);
        Assert.False(localsButton.GetVisualDescendants().OfType<Ellipse>().Single().IsVisible);
        ScrollEnd();
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        Revealed();
        Assert.Contains("locked", globals.Classes);
        Assert.False(globals.IsEnabled);
        vm.ToggleWhiteBalancePickerCommand.Execute(null);
        Assert.False(vm.IsWhiteBalancePicking);
        vm.CurrentCrop = new CropRegion { Left = .2, Right = .8 };
        Assert.False(vm.HasCommittedCrop);
        ScrollEnd();
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        Assert.False(vm.IsCropMode);
        Assert.Null(vm.CurrentCrop);
        Assert.Null(vm.SelectedImage!.EditSettings.Crop);
        Revealed();
        Assert.Contains("locked", globals.Classes);
        Assert.False(globals.IsEnabled);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        await vm.ToggleLocalEnabledCommand.ExecuteAsync(vm.SelectedLocal);
        var saved = vm.SelectedLocal! with { };
        Assert.False(saved.Enabled);
        Assert.Equal(0, saved.Exposure);
        Assert.Equal("contains saved locals", AutomationProperties.GetHelpText(localsButton));
        vm.AddLinearCommand.Execute(null);
        Assert.True(vm.BeginLocalsGesture(LocalHandle.Create, new(.2, .2)));
        vm.MoveLocalsGesture(new(.7, .7), 100);
        ScrollEnd();
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        Assert.False(vm.IsLocalsMode);
        Assert.False(vm.IsLocalCreationArmed);
        Assert.Equal(saved, Assert.Single(vm.Locals));
        Revealed();
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        Recovered();
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        Recovered();
        Assert.True(localsButton.GetVisualDescendants().OfType<Ellipse>().Single().IsVisible);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        vm.CurrentCrop = new CropRegion { Left = .1, Right = .9 };
        await vm.ApplyCropCommand.ExecuteAsync(null);
        Recovered();
        Assert.True(vm.HasCommittedCrop);
        Assert.Equal("contains a committed crop", AutomationProperties.GetHelpText(cropButton));
        await vm.UndoCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.HasCommittedCrop);
        Assert.Equal("", AutomationProperties.GetHelpText(cropButton));
        await vm.RedoCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.HasCommittedCrop);
        Assert.Equal("contains a committed crop", AutomationProperties.GetHelpText(cropButton));

        void ScrollEnd()
        {
            scroll.Offset = new Vector(0, scroll.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            Assert.True(scroll.Offset.Y > 0);
        }
        void Revealed()
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, scroll.Offset.Y);
        }
        void Recovered()
        {
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("locked", globals.Classes);
            Assert.True(globals.IsEnabled);
            foreach (var name in new[] { "RawProfilePicker", "WhiteBalanceControls", "SaturationSlider" })
                Assert.Equal(!mono, panel.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name).IsEffectivelyEnabled);
            Assert.True(viewer.FindControl<Button>("FullScreenButton")!.IsEffectivelyEnabled);
        }
    }

    [AvaloniaFact]
    public async Task HorizonReleaseStaysDraftUntilApply()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        vm.ShowWorkspaceReady(HappyPhoton.ViewModels.MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await Prepare(vm, catalog);
        var panel = new DevelopEditPanel { DataContext = vm };
        using var scope = new TestUiScope(new Window { Width = 250, Height = 660, Content = panel });
        Dispatcher.UIThread.RunJobs();
        var horizon = panel.GetLogicalDescendants().OfType<CompactSlider>().Single(s => s.Label == "Horizon");
        var before = vm.SelectedImage!.EditSettings.Clone();
        var historyCount = vm.HistoryEntries.Count;
        var saves = 0;
        catalog.EditHistoryWriteGateAsync = () => { saves++; return Task.CompletedTask; };
        var escaped = 0;
        panel.AddHandler(CompactSlider.DragStartedEvent, (_, _) => escaped++);
        panel.AddHandler(CompactSlider.DragCompletedEvent, (_, _) => escaped++);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        await Drag();
        Assert.Equal(0, escaped);
        Assert.Equal(0, saves);
        Assert.Equal(historyCount, vm.HistoryEntries.Count);
        Assert.True(before.HasSameEdits(vm.SelectedImage.EditSettings));
        Assert.True(before.HasSameEdits((await catalog.LoadImageStatesAsync([vm.SelectedImage.FilePath]))
            [vm.SelectedImage.FilePath].Single().EditSettings));
        await vm.CancelCropCommand.ExecuteAsync(null);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        if (vm.PendingPreviewDebounceTask is { } cancelledPreview)
            await cancelledPreview.WaitAsync(TestWaits.Condition);
        Assert.True(before.HasSameEdits(vm.SelectedImage.EditSettings));
        Assert.Equal(historyCount, vm.HistoryEntries.Count);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        await Drag();
        await vm.ApplyCropCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.SelectedImage.EditSettings.HorizonRotation);
        Assert.Single(vm.HistoryEntries, e => e.Settings.HorizonRotation == 2);
        Assert.Equal(2, vm.HistoryEntries.Count);
        Assert.Equal(1, saves);

        async Task Drag()
        {
            horizon.RaiseEvent(new RoutedEventArgs(CompactSlider.DragStartedEvent));
            horizon.Value = 2;
            Assert.Equal(2, vm.HorizonRotation);
            horizon.RaiseEvent(new RoutedEventArgs(CompactSlider.DragCompletedEvent));
            clock.Advance(TimeSpan.FromMilliseconds(200));
            if (vm.PendingPreviewDebounceTask is { } preview) await preview.WaitAsync(TestWaits.Condition);
            if (vm.PendingHistoryCommitTask is { } commit) await commit.WaitAsync(TestWaits.Condition);
        }
    }
}

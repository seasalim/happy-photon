using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false, "viewer")]
    [InlineData(true, "viewer")]
    [InlineData(false, "AddLinearButton")]
    [InlineData(true, "AddRadialButton")]
    [InlineData(false, "PlaceLocalAtCenterButton")]
    [InlineData(true, "PlaceLocalAtCenterButton")]
    [InlineData(false, "text")]
    public async Task ArmedEnterThroughWindowCreatesExactlyOnce(bool radial, string focus)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var section = window.GetVisualDescendants().OfType<LocalsEditSection>().Single();
        if (radial) vm.AddRadialCommand.Execute(null);
        else vm.AddLinearCommand.Execute(null);
        var place = section.FindControl<Button>("PlaceLocalAtCenterButton")!;
        Assert.True(place.IsEffectivelyVisible);
        Assert.Equal("Drag to place, or Place at center · Escape cancels", vm.LocalsInstruction);
        Assert.Equal(vm.LocalsInstruction, AutomationProperties.GetName((Control)place.Parent!));
        Control target;
        if (focus == "text")
        {
            // Develop has no standing TextBox; stage one in the real window to exercise its focus route.
            var text = new TextBox();
            ((StackPanel)section.Content!).Children.Add(text);
            target = text;
        }
        else target = focus == "viewer"
            ? window.GetVisualDescendants().OfType<DevelopViewerPane>().Single()
            : section.FindControl<Button>(focus)!;
        if (focus == "viewer") target.Focusable = true;
        target.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Assert.True(target.Focus());
        var count = vm.HistoryEntries.Count;
        Press(Key.Enter);
        if (vm.HandleEnterCommand.ExecutionTask is { } task) await task.WaitAsync(TestWaits.Condition);
        Dispatcher.UIThread.RunJobs();
        if (focus == "text")
        {
            Assert.Empty(vm.Locals);
            Assert.Equal(count, vm.HistoryEntries.Count);
            Assert.True(vm.IsLocalCreationArmed);
            Press(Key.Escape);
            Assert.False(vm.IsLocalCreationArmed);
            Assert.True(vm.IsLocalsMode);
            Assert.Empty(vm.Locals);
            return;
        }
        var local = Assert.Single(vm.Locals);
        Assert.Equal(radial ? "radial" : "linear", local.Type);
        Assert.Equal((.5, .5, radial ? 0d : 90d, radial ? .5 : .25),
            (local.Cu, local.Cv, local.Angle, local.Feather));
        if (radial) Assert.Equal((.25, .25), (local.Rx, local.Ry));
        Assert.Equal(2, vm.HistoryEntries.Count);
        Assert.Single(vm.HistoryEntries, entry => entry.Label == (radial ? "Add Radial" : "Add Linear"));
        Assert.Equal(radial ? "Add Radial" : "Add Linear", vm.HistoryEntries[0].Label);
        Assert.False(vm.IsLocalCreationArmed);
        Assert.False(place.IsEffectivelyVisible);

        void Press(Key key)
        {
            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        }
    }

    [AvaloniaFact]
    public async Task UnarmedEnterKeepsIdleLocalsAndAppliesCrop()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var viewer = window.GetVisualDescendants().OfType<DevelopViewerPane>().Single();
        viewer.Focusable = true;
        Assert.True(viewer.Focus());
        var count = vm.HistoryEntries.Count;
        await Enter();
        Assert.True(vm.IsLocalsMode);
        Assert.Empty(vm.Locals);
        Assert.Equal(count, vm.HistoryEntries.Count);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        vm.CurrentCrop = new CropRegion { Left = .2, Right = .8 };
        await Enter();
        Assert.False(vm.IsCropMode);
        Assert.Equal(.2, vm.SelectedImage!.EditSettings.Crop!.Left);
        Assert.Equal(2, vm.HistoryEntries.Count);
        async Task Enter()
        {
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            if (vm.HandleEnterCommand.ExecutionTask is { } task) await task.WaitAsync(TestWaits.Condition);
        }
    }

    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(8, false)]
    public async Task CreationOrderEmptyDetailsCapAndTabReach(int count, bool radial)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        if (radial) vm.AddRadialCommand.Execute(null);
        for (var i = 0; i < count; i++) await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var section = window.GetVisualDescendants().OfType<LocalsEditSection>().Single();
        var children = ((StackPanel)section.Content!).Children;
        var row = section.FindControl<StackPanel>("LocalCreationRow")!;
        var list = section.FindControl<ListBox>("LocalList")!;
        var center = section.FindControl<Button>("CenterLocalInViewButton")!;
        var disclosure = section.FindControl<ToggleButton>("LocalGeometryDisclosure")!;
        Assert.Same(row, children[0]);
        var details = section.FindControl<StackPanel>("LocalDetailsArea")!;
        var editor = section.FindControl<StackPanel>("LocalEditor")!;
        Assert.Same(details, children[1]);
        Assert.Same(editor, details.Children[0]);
        Assert.Equal(count > 0, editor.IsEffectivelyVisible);
        var editorChildren = editor.Children;
        Assert.Same(list, editorChildren[0]);
        Assert.Equal(120, list.MaxHeight);
        Assert.Equal("Exposure", Assert.IsType<CompactSlider>(editorChildren[1]).Label);
        Assert.Equal(new[] { "Temperature", "Tint", "Saturation" },
            Assert.IsType<StackPanel>(editorChildren[2]).Children.OfType<CompactSlider>().Select(s => s.Label));
        var actions = Assert.IsType<StackPanel>(editorChildren[4]);
        Assert.Equal("Reset adjustments", Assert.IsType<Button>(actions.Children[0]).Content);
        Assert.Same(center, actions.Children[1]);
        Assert.Equal(radial, editorChildren[5].IsVisible);
        Assert.Same(disclosure, editorChildren[6]);
        Assert.IsType<Border>(children.Last());
        Assert.Equal(count < 8, row.IsEffectivelyEnabled);
        Assert.Equal(count > 0, center.IsEffectivelyEnabled);
        if (count == 0)
        {
            Assert.All(editor.GetVisualDescendants().OfType<Control>(), control => Assert.False(control.IsEffectivelyVisible));
            var hint = Assert.IsType<TextBlock>(details.Children[1]);
            Assert.True(hint.IsEffectivelyVisible);
            Assert.Equal(vm.LocalsInstruction, hint.Text);
            Assert.Equal(0, hint.Bounds.Top);
            Assert.True(details.Bounds.Height >= details.MinHeight);
            Assert.Equal("Choose + Linear or + Radial to create a local.", vm.LocalsInstruction);
        }
        if (count == 8)
        {
            Assert.Equal("8 of 8 locals — delete a local to add another", vm.LocalsInstruction);
            Assert.Contains(section.GetVisualDescendants().OfType<TextBlock>(),
                t => t.IsEffectivelyVisible && t.Text == vm.LocalsInstruction);
            return;
        }
        var emptyHeight = details.Bounds.Height;
        vm.AddLinearCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var inline = section.FindControl<Button>("PlaceLocalAtCenterButton")!;
        var instruction = (StackPanel)inline.Parent!;
        var suffix = instruction.Children.Last();
        Assert.True(suffix.TranslatePoint(new Point(suffix.Bounds.Width, 0), section)!.Value.X <= section.Bounds.Width);
        Assert.True(WorkspaceKeyRouting.IsEnterTextInputFocused(new TextBox()));
        Assert.False(WorkspaceKeyRouting.IsEnterTextInputFocused(inline));
        var targets = new List<Control> { section.FindControl<Button>("AddLinearButton")!,
            section.FindControl<Button>("AddRadialButton")!, section.FindControl<Button>("PlaceLocalAtCenterButton")! };
        if (count > 0) targets.Add(center);
        foreach (var target in targets)
        {
            target.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            SettleHit(window, target);
        }
        Assert.True(targets[0].Focus());
        var reached = new HashSet<Control> { targets[0] };
        for (var i = 0; i < 150 && !targets.All(reached.Contains); i++)
        {
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            if (window.FocusManager!.GetFocusedElement() is Control control) reached.Add(control);
        }
        Assert.All(targets, target => Assert.Contains(target, reached));
        if (count == 0)
        {
            Assert.Equal(0, instruction.Bounds.Top);
            Assert.Equal(emptyHeight, details.Bounds.Height);
            // The compact placeholder gives way to the editor on the first local and returns on delete.
            Assert.InRange(emptyHeight, 40, 80);
            foreach (var createRadial in new[] { false, true })
            {
                if (createRadial) vm.AddRadialCommand.Execute(null);
                await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
                Dispatcher.UIThread.RunJobs();
                Assert.True(editor.IsEffectivelyVisible);
                Assert.True(details.Bounds.Height > emptyHeight);
                vm.DeleteLocalCommand.Execute(vm.SelectedLocal);
                Dispatcher.UIThread.RunJobs();
                Assert.False(editor.IsEffectivelyVisible);
                Assert.Equal(emptyHeight, details.Bounds.Height);
            }
        }
    }
}

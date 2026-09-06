using Avalonia;
using Avalonia.Controls;
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
    [InlineData("X", -100, -1)]
    [InlineData("X", 200, 2)]
    [InlineData("Y", -100, -1)]
    [InlineData("Y", 200, 2)]
    [InlineData("Angle", 0, 0)]
    [InlineData("Angle", 360, 0)]
    [InlineData("Angle", -1, 359)]
    [InlineData("Angle", 721, 1)]
    [InlineData("Width", .1, .001)]
    [InlineData("Width", 200, 2)]
    [InlineData("Width", -10, .001)]
    [InlineData("X", 300, 2)]
    public async Task GeometryBoundsCommitOnceAndEqualValuesAreNoOps(string field, double value, double stored)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var property = typeof(MainWindowViewModel).GetProperty("Local" + field)!;
        var before = vm.SelectedLocal! with { };
        var count = vm.HistoryEntries.Count;
        vm.OnSliderEditStarted();
        property.SetValue(vm, value);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(count, vm.HistoryEntries.Count);
        var actual = field switch
        {
            "X" => vm.SelectedLocal!.Cu, "Y" => vm.SelectedLocal!.Cv,
            "Angle" => vm.SelectedLocal!.Angle, _ => vm.SelectedLocal!.Feather
        };
        Assert.InRange(Math.Abs(stored - actual), 0, 1e-12);
        Assert.InRange(Math.Abs((double)property.GetValue(vm)! - stored * (field == "Angle" ? 1 : 100)), 0, 1e-12);
        vm.OnSliderEditCompleted("Local geometry");
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        Assert.Equal("Local geometry", vm.HistoryEntries[0].Label);
        var pending = vm.PendingPreviewDebounceTask;
        property.SetValue(vm, property.GetValue(vm));
        property.SetValue(vm, double.NaN);
        Assert.Same(pending, vm.PendingPreviewDebounceTask);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(before, vm.SelectedLocal);
    }

    [AvaloniaFact]
    public async Task FocusedGeometryAndExposureConsumeArrowsAndDebounceKeyboardBursts()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var image = vm.SelectedImage!;
        vm.Browse.SetImages([image, new ImageFile(_fixture.Path("next.jpg"))]);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        Assert.False(vm.IsLocalGeometryExpanded);
        vm.IsLocalGeometryExpanded = true;
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var section = window.GetVisualDescendants().OfType<LocalsEditSection>().Single();
        var sliders = section.GetVisualDescendants().OfType<CompactSlider>().Where(s => s.IsVisible).ToArray();
        Assert.Equal(8, sliders.Length);
        foreach (var slider in sliders)
        {
            slider.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Assert.True(slider.Focus(NavigationMethod.Tab));
            var old = slider.Value;
            var count = vm.HistoryEntries.Count;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal(old + 2 * slider.SmallChange, slider.Value, 10);
            Assert.Same(image, vm.SelectedImage);
            Assert.Equal(count, vm.HistoryEntries.Count);
            clock.Advance(TimeSpan.FromMilliseconds(200));
            await vm.PendingPreviewDebounceTask!;
            Assert.Equal(count + 1, vm.HistoryEntries.Count);
            if (slider.Classes.Contains("local-geometry")) Assert.Equal("Local geometry", vm.HistoryEntries[0].Label);
            window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.None, null);
            Assert.Equal(old - 8 * slider.SmallChange, slider.Value, 10);
            clock.Advance(TimeSpan.FromMilliseconds(200));
            await vm.PendingPreviewDebounceTask!;
        }
        var angle = sliders.Single(slider => slider.Label == "Angle");
        vm.LocalAngle = 0;
        angle.Focus();
        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal(359, vm.LocalAngle);
        window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal(0, vm.LocalAngle);
        Assert.Equal(0, angle.Value);
        vm.CloseLocalsCommand.Execute(null);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        Assert.True(vm.IsLocalGeometryExpanded);
    }

    [AvaloniaFact]
    public async Task PickerShortcutAndButtonAreInertAndEntryCancelsArmedPicker()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        window.KeyPress(Key.W, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.True(vm.IsWhiteBalancePicking);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        Assert.False(vm.IsWhiteBalancePicking);
        var before = vm.SelectedImage!.EditSettings.Clone();
        window.KeyPress(Key.W, RawInputModifiers.None, PhysicalKey.None, null);
        vm.ToggleWhiteBalancePickerCommand.Execute(null);
        Assert.False(vm.IsWhiteBalancePicking);
        Assert.True(vm.CanEditLocals);
        var button = window.GetVisualDescendants().OfType<Control>()
            .Single(control => control.Name == "WhiteBalancePickerButton");
        Assert.False(button.IsEffectivelyEnabled);
        button.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Click(window, button);
        var overlay = window.GetVisualDescendants().OfType<LocalsOverlayControl>().Single();
        Click(window, overlay);
        Assert.True(before.HasSameEdits(vm.SelectedImage.EditSettings));
        Assert.False(vm.IsWhiteBalancePicking);
        vm.CloseLocalsCommand.Execute(null);
        window.KeyPress(Key.W, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.True(vm.IsWhiteBalancePicking);
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeometryPointerDragPreviewsThenCommitsOnReleaseOrCaptureLoss(bool captureLoss)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        vm.IsLocalGeometryExpanded = true;
        var before = vm.SelectedLocal! with { };
        var count = vm.HistoryEntries.Count;
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var slider = window.GetVisualDescendants().OfType<CompactSlider>()
            .Single(control => control.Name == "LocalXSlider");
        Dispatcher.UIThread.RunJobs();
        slider.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        SettleHit(window, slider);
        Assert.False(slider.EnableDoubleClickReset);
        IPointer? pointer = null;
        window.AddHandler(InputElement.PointerPressedEvent, (_, args) => pointer = args.Pointer,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        var start = slider.TranslatePoint(new Point(120, 10), window)!.Value;
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(start + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        Assert.NotEqual(before.Cu, vm.SelectedLocal!.Cu);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(count, vm.HistoryEntries.Count);
        if (captureLoss) pointer!.Capture(null);
        else window.MouseUp(start + new Vector(10, 0), MouseButton.Left, RawInputModifiers.None);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        Assert.Equal("Local geometry", vm.HistoryEntries[0].Label);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(before, vm.SelectedLocal);
    }
}


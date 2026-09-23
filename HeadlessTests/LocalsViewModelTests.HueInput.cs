using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task HueHeaderAndDegreeControlsAreKeyboardReachableAndWrap()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        vm.ShowWorkspaceReady(HappyPhoton.ViewModels.MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 350, Height = 1000, Content = panel };
        using var scope = new TestUiScope(window);
        var section = panel.GetVisualDescendants().OfType<LocalsEditSection>().Single();
        var checkbox = section.FindControl<CheckBox>("LocalHueEnabled")!;
        Assert.True(checkbox.IsEffectivelyVisible); Assert.False(vm.IsLocalHueExpanded);
        Assert.True(checkbox.Focus()); window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        await TestWaits.UntilAsync(() => vm.IsLocalHueEnabled && !vm.ToggleLocalHueCommand.IsRunning);
        Assert.False(vm.IsLocalHueExpanded);
        vm.IsLocalHueExpanded = true;
        var pick = section.FindControl<Avalonia.Controls.Primitives.ToggleButton>("LocalHuePick")!;
        Assert.True(pick.Focus());
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.True(vm.IsLocalHuePicking); Assert.True(pick.IsChecked);
        var presenter = pick.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single();
        Assert.Equal(ThemeResourceTests.Resource<Avalonia.Media.IBrush>("ControlActive", Avalonia.Styling.ThemeVariant.Dark),
            presenter.Background);
        vm.EscapeLocals();
        Assert.False(pick.IsChecked); Assert.Empty(vm.LocalHuePickAvailability);
        var center = section.GetVisualDescendants().OfType<CompactSlider>().Single(c => c.Label == "Center");
        vm.LocalHueCenter = 359;
        Assert.True(center.Focus(NavigationMethod.Tab)); window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.Equal(0, vm.LocalHueCenter);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal("Hue Range", vm.HistoryEntries[0].Label);
        Assert.Equal("Hue", vm.SelectedLocalRow!.RangeLabel);
        await vm.ToggleLocalLuminanceCommand.ExecuteAsync(null); vm.LocalLuminanceLower = 30;
        Assert.Equal("Luminance · Hue", vm.SelectedLocalRow.RangeLabel);
    }

    [AvaloniaFact]
    public async Task HueClickPrecedesHandleHitAndMismatchingSurfaceCannotSample()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateHueVm(catalog); vm.IsDevelopMode = true;
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var overlay = new LocalsOverlayControl { DataContext = vm, Width = 640, Height = 480 };
        var window = new Window { Width = 640, Height = 480, Content = overlay };
        using var scope = new TestUiScope(window);
        vm.ToggleLocalHuePickCommand.Execute(null);
        var center = LocalsOverlayControl.ToCanvas(vm.SelectedLocal!, vm.LocalsFrame!.Value, overlay.Bounds.Size);
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        Assert.False(vm.IsLocalsGestureActive);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        await TestWaits.UntilAsync(() => vm.HistoryEntries[0].Label == "Pick Hue");
        Assert.False(vm.IsLocalHuePicking);
        var original = vm.PreviewImage;
        using var replacement = new Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(64, 48), new Vector(96, 96));
        vm.PreviewImage = replacement;
        Assert.False(vm.CanPickLocalHue);
        Assert.Contains("matching loaded base", vm.LocalHuePickAvailability);
        vm.ToggleLocalHuePickCommand.Execute(null); Assert.False(vm.IsLocalHuePicking);
        vm.PreviewImage = original;
    }
}

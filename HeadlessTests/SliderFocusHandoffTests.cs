using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SliderFocusHandoffTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("slider-focus");

    [AvaloniaFact]
    public async Task DragReleasesArrowsToPhotoNavigationAndPhotoClickReclaimsThem()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = _fixture.CreateViewModel(catalog, new LocalTestLoader(),
            _ => Task.CompletedTask, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.IsDevelopMode = true;
        var first = new ImageFile(_fixture.Path("photo.jpg"));
        first.CatalogId = await catalog.GetOrCreateImageAsync(first.FilePath);
        var next = new ImageFile(_fixture.Path("next.jpg"));
        next.CatalogId = await catalog.GetOrCreateImageAsync(next.FilePath);
        vm.Browse.SetImages([first, next]);
        vm.SelectedImage = first;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var slider = window.GetVisualDescendants().OfType<CompactSlider>().Single(s =>
            s.Label == "Exposure" && s.IsEffectivelyVisible &&
            !s.GetVisualAncestors().OfType<LocalsEditSection>().Any());
        Dispatcher.UIThread.RunJobs();
        slider.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        SettleHit(window, slider);
        var start = slider.TranslatePoint(new Point(120, 10), window)!.Value;

        // Plain click keeps the slider for keyboard fine-tuning.
        var old = slider.Value;
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(start, MouseButton.Left, RawInputModifiers.None);
        Assert.True(slider.IsFocused);
        Press(Key.Right);
        Assert.Equal(old + slider.SmallChange, slider.Value, 10);
        Assert.Same(first, vm.SelectedImage);

        // Clicking the photo takes focus back; arrows and Space route to the workspace.
        var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
        SettleHit(window, pane.Viewer);
        var photo = Center(pane.Viewer);
        window.MouseDown(photo, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(photo, MouseButton.Left, RawInputModifiers.None);
        Assert.Same(pane, window.FocusManager!.GetFocusedElement());
        var fit = vm.IsZoomFitMode;
        Press(Key.Space);
        Assert.NotEqual(fit, vm.IsZoomFitMode);
        Press(Key.Right);
        Assert.Same(next, vm.SelectedImage);
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);

        // A drag hands focus back so the next arrow navigates.
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        Assert.True(slider.IsFocused);
        window.MouseMove(start + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(10, 0), MouseButton.Left, RawInputModifiers.None);
        Assert.False(slider.IsFocused);
        Assert.Null(window.FocusManager.GetFocusedElement());
        Press(Key.Left);
        Assert.Same(first, vm.SelectedImage);

        // Controls inside the pane still take their own focus.
        var zoom = pane.GetVisualDescendants().OfType<CompactSlider>().Single(s => s.Label == "Zoom");
        SettleHit(window, zoom);
        var zoomPoint = Center(zoom);
        window.MouseDown(zoomPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(zoomPoint, MouseButton.Left, RawInputModifiers.None);
        Assert.Same(zoom, window.FocusManager.GetFocusedElement());

        Point Center(Control control) => control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        void Press(Key key)
        {
            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        }
    }

    private static void SettleHit(Window window, Control control) => ShowcaseTestHelper.Settle(() =>
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var target = window.InputHitTest(center) as Visual;
        return ReferenceEquals(target, control) || target?.GetVisualAncestors().Contains(control) == true;
    }, $"{control.Name} hit-test");

    public void Dispose() => _fixture.Dispose();
}

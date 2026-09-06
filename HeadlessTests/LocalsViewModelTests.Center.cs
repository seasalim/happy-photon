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
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    [InlineData(true, 1.001)]
    public async Task CenterButtonUsesClippedViewportOracle(bool cropped, double zoom)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        if (cropped)
            vm.SelectedImage!.EditSettings.Crop = vm.CurrentCrop =
                new CropRegion { Left = .25, Top = .25, Right = .75, Bottom = .75 };
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        vm.LocalX = -100;
        vm.LocalY = 200;
        vm.LocalExposure = -1;
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        await vm.ToggleLocalEnabledCommand.ExecuteAsync(null);
        var before = vm.SelectedLocal! with { };
        var count = vm.HistoryEntries.Count;
        vm.IsLocalGeometryExpanded = true;
        var viewer = new ZoomPanControl { Source = vm.PreviewImage, DataContext = vm,
            OriginalViewPixelSize = new PixelSize(1600, 1200),
            ScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden };
        viewer.VisibleRegionChanged += (_, region) => vm.PublishNavigatorVisibleRegion(region);
        var section = new LocalsEditSection { DataContext = vm };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,250") };
        grid.Children.Add(viewer);
        Grid.SetColumn(section, 1);
        grid.Children.Add(section);
        using var scope = new TestUiScope(new Window { Width = 1050, Height = 600, Content = grid });
        var window = scope.Window!;
        viewer.ZoomLevel = viewer.GetFitZoomLevel() * zoom;
        Dispatcher.UIThread.RunJobs();
        var scroll = viewer.FindControl<ScrollViewer>("ScrollViewer")!;
        scroll.Offset = zoom < 2 ? new Vector(10000, 0) : new Vector(700, 300);
        Dispatcher.UIThread.RunJobs();
        viewer.RequestVisibleRegionPublication(force: true);
        Dispatcher.UIThread.RunJobs();
        var display = viewer.FindControl<DisplayImage>("ImageControl")!;
        var origin = display.TranslatePoint(default, scroll)!.Value;
        var left = Math.Max(0, origin.X);
        var top = Math.Max(0, origin.Y);
        var right = Math.Min(scroll.Viewport.Width, origin.X + display.Bounds.Width);
        var bottom = Math.Min(scroll.Viewport.Height, origin.Y + display.Bounds.Height);
        var oracle = new Point(((left + right) / 2 - origin.X) / display.Bounds.Width,
            ((top + bottom) / 2 - origin.Y) / display.Bounds.Height);
        if (zoom >= 2) Assert.NotNull(vm.NavigatorVisibleRegion);
        else Assert.Null(vm.NavigatorVisibleRegion);
        if (zoom is > 1 and < 2) Assert.True(Math.Abs(oracle.X - .5) > .0001);
        var button = section.FindControl<Button>("CenterLocalInViewButton")!;
        Assert.True(button.IsEffectivelyEnabled);
        SettleHit(window, button);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        var hit = window.InputHitTest(point) as Visual;
        Assert.True(ReferenceEquals(hit, button) || hit!.GetVisualAncestors().Contains(button));
        Click(window, button);
        Assert.NotNull(vm.CenterLocalInViewCommand.ExecutionTask);
        await vm.CenterLocalInViewCommand.ExecutionTask;
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        Assert.Equal("Center in view", vm.HistoryEntries[0].Label);
        var frame = vm.LocalsFrame!.Value;
        var actual = LocalsOverlayControl.ToCanvas(vm.SelectedLocal!, frame, new Size(1, 1));
        var tolerance = zoom is > 1 and < 2 ? .005 : 1e-9;
        Assert.InRange(Math.Abs(actual.X - oracle.X), 0, tolerance);
        Assert.InRange(Math.Abs(actual.Y - oracle.Y), 0, tolerance);
        Assert.Equal(before with { Cu = vm.SelectedLocal!.Cu, Cv = vm.SelectedLocal.Cv }, vm.SelectedLocal);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(before, vm.SelectedLocal);
    }

    [AvaloniaFact]
    public async Task GeometryControlsAreReachableAndNavigationClearsCenterRegion()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var section = window.GetVisualDescendants().OfType<LocalsEditSection>().Single();
        var disclosure = section.FindControl<Avalonia.Controls.Primitives.ToggleButton>("LocalGeometryDisclosure")!;
        Assert.False(vm.IsLocalGeometryExpanded);
        Assert.Equal("Geometry", Avalonia.Automation.AutomationProperties.GetName(disclosure));
        Assert.False(disclosure.IsEffectivelyVisible);
        Assert.False(section.FindControl<Button>("CenterLocalInViewButton")!.IsEffectivelyEnabled);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(control => control.Name == "DevelopControlsScrollViewer").Offset = default;
        SettleHit(window, disclosure);
        Click(window, disclosure);
        Assert.True(vm.IsLocalGeometryExpanded);
        var controls = section.GetVisualDescendants().OfType<Control>()
            .Where(control => control.Name is "LocalXSlider" or "LocalYSlider" or "LocalAngleSlider"
                or "LocalWidthSlider" or "CenterLocalInViewButton").ToArray();
        Assert.Equal(5, controls.Length);
        foreach (var control in controls)
        {
            Assert.True(control.IsEffectivelyEnabled);
            control.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            SettleHit(window, control);
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            var hit = window.InputHitTest(point) as Visual;
            Assert.True(ReferenceEquals(hit, control) || hit!.GetVisualAncestors().Contains(control),
                $"{control.Name} at {point} hit {hit?.GetType().Name} / {(hit as Control)?.Name}");
            Assert.True(control.Focus(NavigationMethod.Tab));
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.False(control.IsFocused);
        }
        vm.PublishNavigatorVisibleRegion(new Rect(.7, .6, .2, .2));
        Assert.NotNull(vm.NavigatorVisibleRegion);
        var next = new ImageFile(_fixture.Path("next.jpg"))
        {
            CatalogId = await catalog.GetOrCreateImageAsync(_fixture.Path("next.jpg")),
            EditSettings = new EditSettings { Locals = [new() { Cu = -1, Cv = 2 }] }
        };
        await catalog.SaveEditSettingsAsync(next.CatalogId, next.EditSettings);
        vm.SelectedImage = next;
        Assert.Null(vm.NavigatorVisibleRegion);
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.CanEditLocals && vm.PreviewImage != null);
        await vm.CenterLocalInViewCommand.ExecuteAsync(null);
        Assert.Equal(.5, vm.SelectedLocal!.Cu);
        Assert.Equal(.5, vm.SelectedLocal.Cv);
    }

    private static void SettleHit(Window window, Control control) => ShowcaseTestHelper.Settle(() =>
    {
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var target = window.InputHitTest(center) as Visual;
        return ReferenceEquals(target, control) || target?.GetVisualAncestors().Contains(control) == true;
    }, $"{control.Name} expansion and scroll");
}

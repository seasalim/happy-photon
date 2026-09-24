using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task BrushCanvasDabsShiftPinsCaptureLossAndCursorLifecycle()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var gradient = vm.SelectedLocal!;
        var overlay = new LocalsOverlayControl { DataContext = vm, Width = 640, Height = 480 };
        var window = new Window { Width = 640, Height = 480, Content = overlay };
        using var scope = new TestUiScope(window);
        vm.AddBrushCommand.Execute(null);
        window.MouseMove(new(100, 100));
        Assert.Equal("None", overlay.Cursor?.ToString());
        Assert.True(overlay.BrushScreenRadius > 4);
        vm.BrushSize = 1; Assert.True(overlay.BrushScreenRadius < 4);
        vm.BrushSize = 65;
        window.MouseDown(new(100, 100), MouseButton.Left); window.MouseUp(new(100, 100), MouseButton.Left);
        await TestWaits.UntilAsync(() => vm.HistoryEntries[0].Label == "Add Brush");
        var brush = vm.SelectedLocal!;
        Assert.Single(brush.Strokes![0].Points);
        Assert.Null(overlay.HitPin(new(100, 100), vm.LocalsFrame!.Value));
        Assert.Same(gradient, overlay.HitPin(new(320, 240), vm.LocalsFrame.Value));
        window.MouseDown(new(200, 100), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(new(200, 100), MouseButton.Left, RawInputModifiers.Shift);
        await TestWaits.UntilAsync(() => vm.HistoryEntries[0].Label == "Brush stroke");
        Assert.Equal(brush.Strokes[0].Points[0], brush.Strokes[1].Points[0]);
        Assert.Equal(2, brush.Strokes[1].Points.Count);
        window.MouseDown(new(320, 240), MouseButton.Left); window.MouseUp(new(320, 240), MouseButton.Left);
        Assert.Same(gradient, vm.SelectedLocal); Assert.Equal(Cursor.Default, overlay.Cursor);
        vm.SelectedLocal = brush;
        var before = brush with { };
        window.MouseDown(new(110, 110), MouseButton.Left); window.MouseMove(new(150, 200), RawInputModifiers.LeftMouseButton);
        vm.EscapeLocals(); Assert.Equal(before, vm.SelectedLocal); Assert.Equal(Cursor.Default, overlay.Cursor);
        window.MouseMove(new(110, 110)); Assert.Equal("None", overlay.Cursor?.ToString());
        window.MouseDown(new(110, 110), MouseButton.Left); window.MouseMove(new(150, 200), RawInputModifiers.LeftMouseButton);
        window.Content = null;
        Assert.False(vm.IsLocalsGestureActive); Assert.Equal(before, vm.SelectedLocal); Assert.Equal(Cursor.Default, overlay.Cursor);
        window.Content = overlay;
        overlay.UpdateBrushHover(new(-1, 10)); Assert.Equal(Cursor.Default, overlay.Cursor);
        overlay.UpdateBrushHover(new(10, 10)); Assert.Equal("None", overlay.Cursor?.ToString());
        vm.CloseLocalsCommand.Execute(null); Assert.Equal(Cursor.Default, overlay.Cursor);
    }

    [AvaloniaFact]
    public async Task BrushPanelIsVisibleArmedAndEnterDoesNotCreate()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddBrushCommand.Execute(null);
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var section = window.GetVisualDescendants().OfType<LocalsEditSection>().Single();
        Assert.True(section.FindControl<StackPanel>("BrushSection")!.IsEffectivelyVisible);
        Assert.False(section.FindControl<Button>("PlaceLocalAtCenterButton")!.IsEffectivelyVisible);
        window.Focus(); ShortcutPress(window, Key.Enter);
        Assert.Empty(vm.Locals); Assert.True(vm.IsBrushCreationArmed);
        var slider = section.FindControl<StackPanel>("BrushSection")!.Children.OfType<CompactSlider>().First();
        var preview = vm.PendingPreviewDebounceTask;
        var history = vm.HistoryEntries.Count;
        slider.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(CompactSlider.DragStartedEvent));
        slider.Value = 80;
        slider.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(CompactSlider.DragCompletedEvent));
        Assert.Equal(80, vm.BrushSize); Assert.Equal(history, vm.HistoryEntries.Count);
        Assert.Same(preview, vm.PendingPreviewDebounceTask);
    }
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task BrushPreservesWheelMiddlePanAndSuspendsLoupe(bool armed)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.AddBrushCommand.Execute(null);
        if (!armed) { vm.BeginBrushStroke(new(.2, .2)); await vm.CompleteLocalsGestureAsync(); }
        var viewer = new ZoomPanControl { Source = vm.PreviewImage, DataContext = vm,
            IsLocalsMode = true, OriginalViewPixelSize = new PixelSize(1600, 1200) };
        using var sourceBinding = viewer.Bind(ZoomPanControl.SourceProperty, new Avalonia.Data.Binding(nameof(vm.PreviewImage)));
        viewer.SetLoupeTimeProvider(clock);
        viewer.AutoFitRequested += (_, zoom) => viewer.ZoomLevel = zoom;
        viewer.ZoomChanged += (_, delta) => { viewer.AutoFit = false; viewer.ZoomLevel *= delta > 0 ? 1.1 : 1 / 1.1; };
        using var scope = new TestUiScope(new Window { Width = 500, Height = 450, Content = viewer });
        var window = scope.Window!;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var overlay = viewer.FindControl<LocalsOverlayControl>("LocalsOverlay")!;
        SettleHit(window, overlay);
        var point = overlay.TranslatePoint(new Point(overlay.Bounds.Width * .6, overlay.Bounds.Height * .6), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(viewer.IsLoupePeekActive); Assert.True(vm.IsBrushStrokeActive);
        vm.EscapeLocals(); window.MouseUp(point, MouseButton.Left);
        if (armed) vm.AddBrushCommand.Execute(null);
        viewer.AutoFit = false; viewer.ZoomLevel = viewer.GetFitZoomLevel() * 3;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var zoom = viewer.ZoomLevel;
        point = new Point(250, 225);
        window.MouseWheel(point, new Vector(0, 1), RawInputModifiers.None);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(viewer.ZoomLevel > zoom);
        var scroll = viewer.FindControl<ScrollViewer>("ScrollViewer")!;
        scroll.Offset = new Vector(100, 100);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var offset = scroll.Offset;
        window.MouseDown(point, MouseButton.Middle);
        window.MouseMove(point - new Vector(30, 25), RawInputModifiers.MiddleMouseButton);
        window.MouseUp(point - new Vector(30, 25), MouseButton.Middle);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(offset, scroll.Offset);
        Assert.False(vm.IsBrushStrokeActive); Assert.False(viewer.IsLoupePeekActive);
    }

}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SpotsViewModelTests
{
    [AvaloniaFact]
    public async Task RealViewerCreatesByClickAndDragAndSelectsExistingSpots()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog);
        var viewer = new ZoomPanControl { DataContext = vm, Source = vm.PreviewImage,
            IsSpotsMode = true, OriginalViewPixelSize = new PixelSize(1200, 800) };
        viewer.AutoFitRequested += (_, zoom) => viewer.ZoomLevel = zoom;
        using var scope = new TestUiScope(new Window { Width = 600, Height = 450, Content = viewer });
        var window = scope.Window!;
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var overlay = viewer.GetVisualDescendants().OfType<SpotsOverlayControl>().Single();
        Point WindowPoint(Point normalized) => overlay.TranslatePoint(overlay.ToCanvas(normalized), window)!.Value;
        var click = WindowPoint(new(.25, .3));
        window.MouseMove(click);
        Assert.Equal("None", overlay.Cursor?.ToString());
        window.MouseDown(click, MouseButton.Left);
        window.MouseUp(click, MouseButton.Left);
        await TestWaits.UntilAsync(() => vm.HistoryEntries.FirstOrDefault()?.Label == "Add Heal spot");
        var first = Assert.Single(vm.Spots);
        var press = WindowPoint(new(.65, .65));
        var source = WindowPoint(new(.8, .7));
        window.MouseDown(press, MouseButton.Left);
        window.MouseMove(source, RawInputModifiers.LeftMouseButton);
        window.MouseUp(source, MouseButton.Left);
        await vm.PendingHistoryCommitTask!;
        Assert.Equal(2, vm.Spots.Count);
        Assert.InRange(vm.SelectedSpot!.Su, .799, .801);
        window.MouseDown(click, MouseButton.Left);
        window.MouseUp(click, MouseButton.Left);
        Assert.Equal(first.Id, vm.SelectedSpot!.Id);
        Assert.Equal(2, vm.Spots.Count);
    }

    [AvaloniaFact]
    public async Task PreferencesDebounceOutsideHistoryAndSelectedSlidersCommitOnce()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        var saved = new List<AppSettings>();
        vm.PersistAppSettingsAsync = () =>
        {
            var settings = new AppSettings();
            vm.CaptureBrushPreferences(settings);
            saved.Add(settings);
            return System.Threading.Tasks.Task.CompletedTask;
        };
        var section = new SpotsEditSection { DataContext = vm };
        using var scope = new TestUiScope(new Window { Content = section });
        var slider = section.GetVisualDescendants().OfType<CompactSlider>().First(control => control.Label == "Size");
        var history = vm.HistoryEntries.Count;
        var preview = vm.PendingPreviewDebounceTask;
        slider.RaiseEvent(new RoutedEventArgs(CompactSlider.DragStartedEvent));
        slider.Value = 5;
        vm.SpotFeather = 20;
        vm.SpotOpacity = 70;
        await vm.SetSpotModeCommand.ExecuteAsync("clone");
        slider.RaiseEvent(new RoutedEventArgs(CompactSlider.DragCompletedEvent));
        Assert.Equal(history, vm.HistoryEntries.Count);
        Assert.Same(preview, vm.PendingPreviewDebounceTask);
        clock.Advance(TimeSpan.FromMilliseconds(249));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(saved);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await TestWaits.UntilAsync(() => saved.Count == 1);
        Assert.Equal(("clone", 5d, 20d, 70d), (saved[0].SpotMode, saved[0].SpotSize, saved[0].SpotFeather, saved[0].SpotOpacity));
        await CreateSpot(vm);
        Assert.Equal("clone", vm.SelectedSpot!.Type);
        history = vm.HistoryEntries.Count;
        slider.RaiseEvent(new RoutedEventArgs(CompactSlider.DragStartedEvent));
        slider.Value = 4;
        slider.Value = 3;
        slider.RaiseEvent(new RoutedEventArgs(CompactSlider.DragCompletedEvent));
        clock.Advance(TimeSpan.FromSeconds(1));
        await vm.PendingHistoryCommitTask!;
        Assert.Equal(history + 1, vm.HistoryEntries.Count);
        Assert.Equal("Spot size", vm.HistoryEntries[0].Label);
        Assert.Single(saved);
        Assert.Equal(5, vm.NewSpotRadius * 100);
        vm.SelectedSpot = null;
        Assert.Equal(5, vm.SpotSize);
    }
}

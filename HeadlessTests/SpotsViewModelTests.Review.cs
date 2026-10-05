using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.Views;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SpotsViewModelTests
{
    [AvaloniaFact]
    public async Task SpotSliderEscapeReleaseThenExposureStillSaves()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        await CreateSpot(vm, true);
        var original = vm.SelectedSpot! with { };
        var history = vm.HistoryEntries.Count;
        var section = new SpotsEditSection { DataContext = vm };
        using var scope = new TestUiScope(new Window { Content = section });
        var slider = section.GetVisualDescendants().OfType<CompactSlider>().Single(s => s.Label == "Size");

        slider.RaiseEvent(new RoutedEventArgs(CompactSlider.DragStartedEvent));
        slider.Value = 5;
        Assert.NotEqual(original.Radius, vm.Spots[0].Radius);
        Assert.True(vm.EscapeSpots());
        slider.RaiseEvent(new RoutedEventArgs(CompactSlider.DragCompletedEvent));
        vm.Exposure = 1;
        clock.Advance(TimeSpan.FromSeconds(1));
        await vm.PendingPreviewDebounceTask!;

        Assert.Equal(history + 1, vm.HistoryEntries.Count);
        Assert.StartsWith("Exposure", vm.HistoryEntries[0].Label);
        Assert.Equal(1, vm.HistoryEntries[0].Settings.Exposure);
        var saved = Assert.Single((await catalog.LoadImageStatesAsync([vm.SelectedImage!.FilePath]))[vm.SelectedImage.FilePath]);
        Assert.Equal(1, saved.EditSettings.Exposure);
        Assert.Equal(original, Assert.Single(saved.EditSettings.Repairs!));
        Assert.Equal(original, vm.Spots[0]);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.Exposure);
        Assert.Equal(original, vm.Spots[0]);
    }

    [AvaloniaTheory]
    [InlineData("escape")]
    [InlineData("deselect")]
    [InlineData("navigation")]
    [InlineData("rebind")]
    [InlineData("detach")]
    [InlineData("undo")]
    public async Task InterruptedRealSliderDragRollsBackAndReleasesCapture(string interruption)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        await CreateSpot(vm, true);
        var image = vm.SelectedImage!;
        var original = vm.SelectedSpot! with { };
        var history = vm.HistoryEntries.Count;
        var preference = vm.NewSpotRadius;
        var section = new SpotsEditSection { DataContext = vm };
        using var scope = new TestUiScope(new Window { Width = 350, Height = 300, Content = section });
        var window = scope.Window!;
        var slider = section.GetVisualDescendants().OfType<CompactSlider>().Single(s => s.Label == "Size");
        var press = slider.TranslatePoint(new Point(slider.Bounds.Width / 2, slider.Bounds.Height / 2), window)!.Value;
        var end = press + new Vector(30, 0);
        window.MouseDown(press, MouseButton.Left);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        Assert.NotEqual(original.Radius, vm.Spots[0].Radius);

        switch (interruption)
        {
            case "escape": Assert.True(vm.EscapeSpots()); break;
            case "deselect": vm.SelectedSpot = null; break;
            case "navigation": vm.SelectedImage = null; break;
            case "rebind": section.DataContext = null; break;
            case "detach": window.Content = null; break;
            case "undo": await vm.UndoCommand.ExecuteAsync(null); break;
        }
        window.MouseMove(end + new Vector(20, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(end + new Vector(20, 0), MouseButton.Left);
        Assert.False(vm.IsSpotsGestureActive);
        Assert.Equal(original, Assert.Single(image.EditSettings.Repairs!));
        Assert.Equal(preference, vm.NewSpotRadius);
        if (interruption == "navigation")
        {
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        }
        Assert.Equal(history, vm.HistoryEntries.Count);
        vm.Exposure = 1;
        clock.Advance(TimeSpan.FromSeconds(1));
        await vm.PendingPreviewDebounceTask!;
        Assert.Equal(history + 1, vm.HistoryEntries.Count);
        Assert.StartsWith("Exposure", vm.HistoryEntries[0].Label);
        Assert.Equal(original, vm.Spots[0]);
    }

    [AvaloniaFact]
    public async Task StoredSourceUsesClampedDrawingHitTargetAndDragOrigin()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog);
        await CreateSpot(vm, true);
        var spot = vm.SelectedSpot!;
        spot.Su = 0;
        spot.Sv = .6;
        var map = vm.SpotDisplayMap!;
        var (u, v) = RepairGeometry.ClampSource(spot, map.BaseWidth, map.BaseHeight);
        var overlay = new SpotsOverlayControl { DataContext = vm, Width = 600, Height = 400 };
        using var scope = new TestUiScope(new Window { Width = 600, Height = 400, Content = overlay });
        var actual = Draw();
        Assert.Equal(0, spot.Su);
        spot.Su = u;
        spot.Sv = v;
        var expected = Draw();
        spot.Su = 0;
        Assert.Equal(expected, actual);
        var center = overlay.ToCanvas(new(u, v));
        Assert.Equal(SpotHandle.Source, overlay.HitHandle(center, spot));
        Assert.Null(overlay.HitHandle(overlay.ToCanvas(new(0, v)), spot));

        var window = scope.Window!;
        window.MouseDown(center, MouseButton.Left);
        window.MouseMove(center + new Vector(12, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(center + new Vector(12, 0), MouseButton.Left);
        await vm.PendingHistoryCommitTask!;
        Assert.InRange(vm.SelectedSpot!.Su, u + .02 - .0001, u + .02 + .0001);

        byte[] Draw()
        {
            using var target = new RenderTargetBitmap(new PixelSize(600, 400), new Vector(96, 96));
            target.Render(overlay);
            using var stream = new MemoryStream();
            target.Save(stream, PngBitmapEncoderOptions.Default);
            return stream.ToArray();
        }
    }

    [AvaloniaFact]
    public async Task EdgeBandClickDoesNotResizeAndDragScalesFromGrabPoint()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog);
        await CreateSpot(vm, true);
        var spot = vm.SelectedSpot!;
        var radius = spot.Radius;
        var history = vm.HistoryEntries.Count;
        var overlay = new SpotsOverlayControl { DataContext = vm, Width = 600, Height = 400 };
        using var scope = new TestUiScope(new Window { Width = 600, Height = 400, Content = overlay });
        var window = scope.Window!;
        var center = overlay.ToCanvas(new(spot.U, spot.V));
        var grab = center + new Vector(radius * 600 * 1.2, 0);
        Assert.Equal(SpotHandle.Edge, overlay.HitHandle(grab, spot));

        window.MouseDown(grab, MouseButton.Left);
        window.MouseUp(grab, MouseButton.Left);
        await vm.PendingHistoryCommitTask!;
        Assert.Equal(radius, vm.SelectedSpot!.Radius);
        Assert.Equal(history, vm.HistoryEntries.Count);

        window.MouseDown(grab, MouseButton.Left);
        window.MouseMove(grab + new Vector(2, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(grab + new Vector(2, 0), MouseButton.Left);
        await vm.PendingHistoryCommitTask!;
        Assert.Equal(radius, vm.SelectedSpot!.Radius);
        Assert.Equal(history, vm.HistoryEntries.Count);

        window.MouseDown(grab, MouseButton.Left);
        var end = center + (grab - center) * 1.5;
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
        await vm.PendingHistoryCommitTask!;
        Assert.Equal(radius * 1.5, vm.SelectedSpot!.Radius, 8);
        Assert.Equal(history + 1, vm.HistoryEntries.Count);
        Assert.Equal("Resize spot", vm.HistoryEntries[0].Label);
    }
}

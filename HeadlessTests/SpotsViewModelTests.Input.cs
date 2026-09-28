using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SpotsViewModelTests
{
    [AvaloniaFact]
    public async Task RealWindowDeleteNeverTrashesAndTextKeepsEditingKeys()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        var trash = 0;
        vm.ConfirmDeleteAsync = _ => { trash++; return Task.FromResult(false); };
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        Press(Key.Delete);
        await vm.DeleteImageCommand.ExecutionTask!;
        Assert.Equal(0, trash);
        await CreateSpot(vm);
        Press(Key.Delete);
        await vm.DeleteImageCommand.ExecutionTask!;
        Assert.Empty(vm.Spots);
        Assert.Equal(0, trash);
        await CreateSpot(vm);
        Press(Key.Back);
        await vm.DeleteSpotCommand.ExecutionTask!;
        Assert.Empty(vm.Spots);
        await CreateSpot(vm);
        var content = window.Content;
        var text = new TextBox();
        window.Content = text;
        Dispatcher.UIThread.RunJobs();
        Assert.True(text.Focus());
        text.Text = "123";
        text.CaretIndex = 0;
        Press(Key.Delete);
        Assert.Equal("23", text.Text);
        text.CaretIndex = 2;
        Press(Key.Back);
        Assert.Equal("2", text.Text);
        Assert.Single(vm.Spots);
        Assert.Equal(0, trash);
        window.Content = content;
        Assert.True(window.Focus());
        Press(Key.Q);
        Assert.False(vm.IsSpotsMode);
        Press(Key.Q);
        await vm.ToggleSpotsModeCommand.ExecutionTask!;
        Assert.True(vm.IsSpotsMode);

        void Press(Key key)
        {
            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task MinimumRadiusRealPointerTargetsAndCaptureLoss()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog);
        vm.SpotSize = .2;
        await CreateSpot(vm, true);
        var overlay = new SpotsOverlayControl { DataContext = vm, Width = 600, Height = 400 };
        using var scope = new TestUiScope(new Window { Width = 600, Height = 400, Content = overlay });
        var window = scope.Window!;
        foreach (var handle in new[] { SpotHandle.Destination, SpotHandle.Source, SpotHandle.Edge })
        {
            var spot = vm.SelectedSpot!;
            var center = handle == SpotHandle.Source ? new Point(spot.Su, spot.Sv) : new Point(spot.U, spot.V);
            var start = overlay.ToCanvas(center);
            if (handle == SpotHandle.Edge) start += new Vector(spot.Radius * 600, 0);
            Assert.Equal(handle, overlay.HitHandle(start, spot));
            var before = spot with { };
            window.MouseDown(start, MouseButton.Left);
            Assert.True(vm.IsSpotsGestureActive);
            window.MouseMove(start + new Vector(10, 4), RawInputModifiers.LeftMouseButton);
            Assert.NotEqual(before, vm.SelectedSpot);
            window.MouseUp(start + new Vector(10, 4), MouseButton.Left);
            await vm.PendingHistoryCommitTask!;
            Assert.False(vm.IsSpotsGestureActive);
        }
        var saved = vm.SelectedSpot! with { };
        var point = overlay.ToCanvas(new(saved.U, saved.V));
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(20, 20), RawInputModifiers.LeftMouseButton);
        window.Content = null;
        Assert.False(vm.IsSpotsGestureActive);
        Assert.Equal(saved, vm.SelectedSpot);
    }
}

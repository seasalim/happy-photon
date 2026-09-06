using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task CompletedGesturePersistsAfterNavigationDuringRender()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var image = vm.SelectedImage!;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        vm.ImageService.Previews.RenderGateAsync = () =>
        {
            if (Interlocked.Increment(ref count) != 1) return Task.CompletedTask;
            started.TrySetResult();
            return release.Task;
        };
        try
        {
            vm.AddLinearCommand.Execute(null);
            Assert.True(vm.BeginLocalsGesture(LocalHandle.Create, new(.2, .2)));
            vm.MoveLocalsGesture(new(.8, .8), 100);
            var expected = vm.SelectedLocal! with { };
            var completed = vm.CompleteLocalsGestureAsync();
            await started.Task.WaitAsync(TestWaits.Condition);
            Assert.False(vm.IsLocalsGestureActive);
            vm.SelectedImage = new ImageFile(_fixture.Path("next.jpg"));
            release.TrySetResult();
            await completed.WaitAsync(TestWaits.Condition);
            var persisted = (await catalog.LoadImageStatesAsync([image.FilePath]))
                [image.FilePath].Single().EditSettings;
            Assert.Equal(expected, Assert.Single(persisted.Locals ?? []));
            Assert.Empty(vm.Locals);
        }
        finally { release.TrySetResult(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanvasCreatesOnlyWhenArmedAndCaptureLossRestores(bool radial)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var overlay = new LocalsOverlayControl { DataContext = vm, Width = 640, Height = 480 };
        var window = new Window { Width = 640, Height = 480, Content = overlay };
        using var scope = new TestUiScope(window);
        window.MouseDown(new(100, 100), MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(new(100, 100), MouseButton.Left, RawInputModifiers.None);
        Assert.Empty(vm.Locals);
        if (radial) vm.AddRadialCommand.Execute(null);
        else vm.AddLinearCommand.Execute(null);
        window.MouseDown(new(100, 100), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new(400, 350), RawInputModifiers.LeftMouseButton);
        Assert.True(vm.IsLocalsGestureActive);
        window.MouseUp(new(400, 350), MouseButton.Left, RawInputModifiers.None);
        await TestWaits.UntilAsync(() => vm.HistoryEntries.Count == 2);
        Assert.Single(vm.Locals);
        var before = vm.SelectedLocal! with { };
        var frame = vm.LocalsFrame!.Value;
        var center = LocalsOverlayControl.ToCanvas(before, frame, overlay.Bounds.Size);
        Assert.Equal(LocalHandle.Center, overlay.HitHandle(center, before, frame));
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(center + new Vector(30, 30), RawInputModifiers.LeftMouseButton);
        Assert.NotEqual(before.Cu, vm.SelectedLocal!.Cu);
        window.Content = null;
        Assert.False(vm.IsLocalsGestureActive);
        Assert.Equal(before, vm.SelectedLocal);
    }

    [AvaloniaFact]
    public async Task FailedGeometryRenderRestoresCommittedLocal()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        var before = vm.SelectedLocal! with { };
        vm.ImageService.Previews.RenderGateAsync = () => throw new InvalidOperationException("test render failure");
        vm.BeginLocalsGesture(LocalHandle.Center, new(.5, .5));
        vm.MoveLocalsGesture(new(.8, .7), 100);
        await vm.CompleteLocalsGestureAsync();
        Assert.Equal(before, vm.SelectedLocal);
        Assert.Equal(2, vm.HistoryEntries.Count);
    }
}

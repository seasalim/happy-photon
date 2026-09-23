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
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerShortcutsYieldToTextEntryAndReleaseAfterFocusMoves(bool rangeEndpoint)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await PrepareRangeEntry(vm, catalog);
        var window = new MainWindow { Focusable = true, Width = 1200, Height = 900 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        TextBox entry;
        if (rangeEndpoint) entry = FocusRangeEntry(window, 0);
        else
        {
            entry = new TextBox { Text = "draft" };
            window.Content = entry;
            Assert.True(entry.Focus());
        }
        var before = await ShortcutSnapshot(vm, catalog);
        ShortcutPress(window, Key.W, RawInputModifiers.Shift);
        ShortcutPress(window, Key.O);
        ShortcutDown(window, Key.M);
        Assert.True(vm.IsLocalsMode);
        Assert.False(vm.ShowLocalMask);
        Assert.False(vm.IsLocalMaskHeld);
        ShortcutUp(window, Key.M);
        Assert.True(window.Focus());
        ShortcutDown(window, Key.M);
        Assert.True(vm.IsLocalMaskHeld);
        Assert.True(vm.IsLocalMaskVisible);
        Assert.True(entry.Focus());
        Assert.True(vm.IsLocalMaskHeld);
        Assert.True(vm.IsLocalMaskVisible);
        ShortcutUp(window, Key.M);
        Assert.False(vm.IsLocalMaskHeld);
        Assert.False(vm.IsLocalMaskVisible);
        vm.CloseLocalsCommand.Execute(null);
        if (!rangeEndpoint)
        {
            Assert.True(entry.Focus());
            ShortcutPress(window, Key.W, RawInputModifiers.Shift);
            Assert.False(vm.IsLocalsMode);
        }
        await AssertShortcutSnapshot(vm, catalog, before);
    }

    [AvaloniaFact]
    public async Task ViewerMaskHoldSurvivesFocusMovesWithinWindow()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await PrepareShortcutLocal(vm, catalog);
        var window = new MainWindow { Focusable = true, Width = 1200, Height = 900 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var slider = window.GetVisualDescendants().OfType<CompactSlider>()
            .First(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled);
        Assert.True(window.Focus());
        var before = await ShortcutSnapshot(vm, catalog);
        ShortcutDown(window, Key.M);
        Assert.True(vm.IsLocalMaskHeld);
        Assert.True(slider.Focus());
        Assert.Same(slider, window.FocusManager!.GetFocusedElement());
        Assert.True(vm.IsLocalMaskHeld);
        Assert.True(vm.IsLocalMaskVisible);
        Assert.False(vm.ShowLocalMask);
        Assert.True(window.Focus());
        Assert.True(vm.IsLocalMaskHeld);
        ShortcutUp(window, Key.M);
        Assert.False(vm.IsLocalMaskHeld);
        Assert.False(vm.IsLocalMaskVisible);
        await AssertShortcutSnapshot(vm, catalog, before);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerShortcutsPreserveCapturedGestureUntilToolCloses(bool creation)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareShortcutLocal(vm, catalog);
        var window = new MainWindow { Focusable = true, Width = 1200, Height = 900 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Dispatcher.UIThread.RunJobs();
        var overlay = window.GetVisualDescendants().OfType<LocalsOverlayControl>().Single(c => c.IsEffectivelyVisible);
        var center = LocalsOverlayControl.ToCanvas(vm.SelectedLocal!, vm.LocalsFrame!.Value, overlay.Bounds.Size);
        var point = overlay.TranslatePoint(center, window)!.Value;
        IPointer? pointer = null;
        overlay.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var before = await ShortcutSnapshot(vm, catalog);
        if (creation) vm.AddLinearCommand.Execute(null);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(point + new Vector(30, 30), RawInputModifiers.LeftMouseButton);
        Assert.True(vm.IsLocalsGestureActive);
        Assert.NotNull(pointer);
        Assert.Same(overlay, pointer.Captured);
        Assert.Equal(creation, vm.IsLocalMaskVisible);
        var draft = vm.SelectedLocal! with { };
        ShortcutPress(window, Key.O);
        Assert.True(vm.ShowLocalMask);
        Assert.True(vm.IsLocalMaskVisible);
        ShortcutPress(window, Key.O);
        Assert.False(vm.ShowLocalMask);
        ShortcutDown(window, Key.M);
        Assert.True(vm.IsLocalMaskVisible);
        Assert.True(vm.IsLocalMaskHeld);
        ShortcutUp(window, Key.M);
        Assert.Equal(creation, vm.IsLocalMaskVisible);
        Assert.Equal(draft, vm.SelectedLocal);
        Assert.True(vm.IsLocalsGestureActive);
        Assert.Same(overlay, pointer.Captured);
        var during = await ShortcutSnapshot(vm, catalog);
        Assert.Equal(before.History, during.History);
        Assert.Equal(before.Saved, during.Saved);
        ShortcutDown(window, Key.M);
        ShortcutPress(window, Key.W, RawInputModifiers.Shift);
        Assert.False(vm.IsLocalsMode);
        Assert.False(vm.IsLocalMaskHeld);
        Assert.False(vm.IsLocalsGestureActive);
        window.MouseUp(point + new Vector(30, 30), MouseButton.Left, RawInputModifiers.None);
        ShortcutUp(window, Key.M);
        await AssertShortcutSnapshot(vm, catalog, before);
    }

    [AvaloniaFact]
    public async Task ViewerMaskHoldUsesExistingRangeMaskPath()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await PrepareRangeEntry(vm, catalog);
        vm.LocalLuminanceLower = 47;
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await vm.PendingPreviewDebounceTask!;
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        var before = await ShortcutSnapshot(vm, catalog);
        Assert.Null(vm.LocalRangeMask);
        ShortcutDown(window, Key.M);
        await vm.PendingLocalMaskTask;
        Assert.True(vm.IsLocalMaskVisible);
        Assert.NotNull(vm.LocalRangeMask);
        Assert.False(vm.ShowLocalMask);
        ShortcutUp(window, Key.M);
        Assert.False(vm.IsLocalMaskVisible);
        Assert.Null(vm.LocalRangeMask);
        await AssertShortcutSnapshot(vm, catalog, before);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrushShortcutArmsFromClosedOrOpenLocals(bool open)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        if (open) await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        var before = await ShortcutSnapshot(vm, catalog);
        ShortcutPress(window, Key.B);
        await TestWaits.UntilAsync(() => vm.IsBrushCreationArmed);
        Assert.True(vm.IsLocalsMode);
        Assert.True(vm.IsBrushSectionVisible);
        await AssertShortcutSnapshot(vm, catalog, before);
    }

    [AvaloniaTheory]
    [InlineData("cap")]
    [InlineData("original")]
    [InlineData("split")]
    [InlineData("unavailable")]
    [InlineData("fullscreen")]
    [InlineData("browse")]
    [InlineData("empty")]
    public async Task BrushShortcutUnavailableLeavesClosedLocalsAndCropUntouched(string reason)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        if (reason == "cap") vm.SelectedImage!.EditSettings.Locals = Enumerable.Range(0, 8)
            .Select(i => new LocalAdjustment { Id = i.ToString() }).ToList();
        if (reason == "unavailable") vm.ApplyThumbnailLoadStatus(vm.SelectedImage!, ThumbnailLoadStatus.DeferredForHydration);
        if (reason == "fullscreen") vm.IsFullScreenMode = true;
        if (reason == "browse") vm.WorkspaceMode = WorkspaceMode.Browse;
        if (reason == "empty") vm.SelectedImage = null;
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        if (reason == "original")
        {
            vm.Exposure = 1;
            Assert.True(vm.ToggleBeforeAfterCommand.CanExecute(null));
            await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        }
        if (reason == "split") await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
        var crop = reason is not ("original" or "split");
        vm.IsCropMode = crop;
        Assert.False(vm.CanArmBrushFromShortcut);
        ShortcutPress(window, Key.B);
        Assert.False(vm.IsLocalsMode);
        Assert.False(vm.IsBrushCreationArmed);
        Assert.Equal(crop, vm.IsCropMode);
        Assert.Null(vm.ToggleLocalsModeCommand.ExecutionTask);
    }

    [AvaloniaFact]
    public async Task BrushShortcutAwaitsCropCancelAndIgnoresRepeat()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        ShortcutPress(window, Key.B);
        var opening = Assert.IsAssignableFrom<Task>(vm.ToggleLocalsModeCommand.ExecutionTask);
        Assert.False(opening.IsCompleted);
        Assert.False(vm.IsBrushCreationArmed);
        ShortcutPress(window, Key.B);
        Assert.Same(opening, vm.ToggleLocalsModeCommand.ExecutionTask);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        await opening.WaitAsync(TestWaits.Condition);
        await TestWaits.UntilAsync(() => vm.IsBrushCreationArmed);
        Assert.False(vm.IsCropMode);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrushShortcutDoesNotDiscardActiveGesture(bool brush)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareShortcutLocal(vm, catalog);
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        if (brush)
        {
            vm.AddBrushCommand.Execute(null);
            Assert.True(vm.BeginBrushStroke(new(.2, .2)));
        }
        else Assert.True(vm.BeginLocalsGesture(LocalHandle.Center, new(.5, .5)));
        var before = vm.SelectedLocal! with { };
        ShortcutPress(window, Key.B);
        Assert.True(vm.IsLocalsGestureActive);
        Assert.Equal(before, vm.SelectedLocal);
    }

    [AvaloniaFact]
    public async Task BrushShortcutBracketsScaleRadiusAndStepFeatherWithClamps()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareShortcutLocal(vm, catalog);
        vm.AddBrushCommand.Execute(null);
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        var before = await ShortcutSnapshot(vm, catalog);
        vm.BrushSize = 50;
        var radius = vm.BrushRadius;
        ShortcutPress(window, Key.OemOpenBrackets);
        Assert.Equal(.8, vm.BrushRadius / radius, 10);
        ShortcutPress(window, Key.OemCloseBrackets);
        Assert.Equal(radius, vm.BrushRadius, 10);
        ShortcutPress(window, Key.OemCloseBrackets);
        Assert.Equal(1.25, vm.BrushRadius / radius, 10);
        vm.BrushFeather = 50;
        ShortcutPress(window, Key.OemOpenBrackets, RawInputModifiers.Shift);
        Assert.Equal(40, vm.BrushFeather);
        ShortcutPress(window, Key.OemCloseBrackets, RawInputModifiers.Shift);
        Assert.Equal(50, vm.BrushFeather);
        foreach (var upper in new[] { false, true })
        {
            vm.BrushSize = upper ? 99 : 2;
            vm.BrushFeather = upper ? 95 : 5;
            var key = upper ? Key.OemCloseBrackets : Key.OemOpenBrackets;
            ShortcutPress(window, key);
            ShortcutPress(window, key, RawInputModifiers.Shift);
            Assert.Equal(upper ? 100 : 1, vm.BrushSize);
            Assert.Equal(upper ? 100 : 0, vm.BrushFeather);
        }
        await AssertShortcutSnapshot(vm, catalog, before);
    }

    [AvaloniaFact]
    public async Task BrushShortcutsFallThroughWhenEditingIsDisabled()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareShortcutLocal(vm, catalog);
        vm.AddBrushCommand.Execute(null);
        Assert.True(vm.BeginBrushStroke(new(.2, .2)));
        await vm.CompleteLocalsGestureAsync();
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
        Assert.True(window.Focus());
        Assert.True(vm.IsBrushSectionVisible);
        Assert.False(vm.CanEditLocals);
        vm.BrushSize = vm.BrushFeather = 50;
        var unhandled = new List<Key>();
        window.AddHandler(InputElement.KeyDownEvent, (_, e) => unhandled.Add(e.Key),
            Avalonia.Interactivity.RoutingStrategies.Bubble);
        foreach (var key in new[] { Key.OemOpenBrackets, Key.OemCloseBrackets })
        {
            ShortcutPress(window, key);
            Assert.Equal(50, vm.BrushSize);
            ShortcutPress(window, key, RawInputModifiers.Shift);
            Assert.Equal(50, vm.BrushFeather);
        }
        foreach (var alt in new[] { Key.LeftAlt, Key.RightAlt })
        {
            ShortcutDown(window, alt, RawInputModifiers.Alt);
            Assert.False(vm.IsBrushAltHeld);
            ShortcutUp(window, alt);
        }
        Assert.Equal(new[] { Key.OemOpenBrackets, Key.OemOpenBrackets,
            Key.OemCloseBrackets, Key.OemCloseBrackets, Key.LeftAlt, Key.RightAlt }, unhandled);
        ShortcutPress(window, Key.O);
        Assert.True(vm.ShowLocalMask);
        ShortcutDown(window, Key.M);
        Assert.True(vm.IsLocalMaskHeld);
        ShortcutUp(window, Key.M);
    }

    [AvaloniaTheory]
    [InlineData("text")]
    [InlineData("hue")]
    [InlineData("whitebalance")]
    [InlineData("hidden")]
    public async Task BrushShortcutsRespectTextPickersAndHiddenSection(string suppression)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareShortcutLocal(vm, catalog);
        if (suppression != "hidden")
        {
            vm.AddBrushCommand.Execute(null);
            Assert.True(vm.BeginBrushStroke(new(.2, .2)));
            await vm.CompleteLocalsGestureAsync();
        }
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        if (suppression == "text")
        {
            var entry = new TextBox { Text = "draft" };
            window.Content = entry;
            Assert.True(entry.Focus());
        }
        if (suppression == "hue")
        {
            vm.ToggleLocalHuePickCommand.Execute(null);
            Assert.True(vm.IsLocalHuePicking);
        }
        if (suppression == "whitebalance")
        {
            vm.CloseLocalsCommand.Execute(null);
            vm.ToggleWhiteBalancePickerCommand.Execute(null);
            Assert.True(vm.IsWhiteBalancePicking);
        }
        var size = vm.BrushSize;
        var feather = vm.BrushFeather;
        foreach (var key in new[] { Key.OemOpenBrackets, Key.OemCloseBrackets })
        {
            ShortcutPress(window, key);
            Assert.Equal(size, vm.BrushSize);
            ShortcutPress(window, key, RawInputModifiers.Shift);
            Assert.Equal(feather, vm.BrushFeather);
        }
        ShortcutDown(window, Key.LeftAlt, RawInputModifiers.Alt);
        Assert.False(vm.IsBrushAltHeld);
        ShortcutUp(window, Key.LeftAlt);
        if (suppression != "hidden") ShortcutPress(window, Key.B);
        Assert.False(vm.IsBrushCreationArmed);
        Assert.Equal(size, vm.BrushSize);
        Assert.Equal(feather, vm.BrushFeather);
        Assert.Equal(suppression == "hue", vm.IsLocalHuePicking);
        Assert.Equal(suppression == "whitebalance", vm.IsWhiteBalancePicking);
    }

    [AvaloniaTheory]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.RightAlt)]
    public async Task BrushShortcutAltOnlyChangesCursorAndClearsOnFocusLoss(Key alt)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await PrepareShortcutLocal(vm, catalog);
        vm.AddBrushCommand.Execute(null);
        vm.BrushMode = "paint";
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        foreach (var exit in new[] { "keyup", "deactivate", "focus" })
        {
            Assert.True(window.Focus());
            ShortcutDown(window, alt, RawInputModifiers.Alt);
            Assert.True(vm.IsBrushAltHeld);
            Assert.False(vm.IsEffectiveBrushPaint);
            Assert.True(vm.IsBrushPaint);
            Assert.False(vm.IsBrushErase);
            var paint = window.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "BrushPaintButton");
            Assert.True(paint.IsChecked);
            switch (exit)
            {
                case "keyup": ShortcutUp(window, alt); break;
                case "deactivate": typeof(WindowBase).GetMethod("HandleDeactivated",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null); break;
                case "focus": window.FocusManager!.Focus(null); break;
            }
            Assert.False(vm.IsBrushAltHeld);
            Assert.True(vm.IsEffectiveBrushPaint);
            ShortcutUp(window, alt);
        }
    }

    [AvaloniaTheory]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.RightAlt)]
    public async Task BrushShortcutShiftThenAltShowsEraseAndCommitsStraightErase(Key alt)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareShortcutLocal(vm, catalog);
        vm.AddBrushCommand.Execute(null);
        vm.BrushMode = "paint";
        var overlay = new LocalsOverlayControl { DataContext = vm, Width = 640, Height = 480 };
        var window = new MainWindow { Focusable = true, Width = 640, Height = 480 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        window.Content = overlay;
        Dispatcher.UIThread.RunJobs();
        window.MouseDown(new(100, 100), MouseButton.Left);
        window.MouseUp(new(100, 100), MouseButton.Left);
        await TestWaits.UntilAsync(() => vm.HistoryEntries[0].Label == "Add Brush");
        var previousEnd = vm.SelectedLocal!.Strokes![0].Points[^1];
        ShortcutDown(window, Key.LeftShift, RawInputModifiers.Shift);
        ShortcutDown(window, alt, RawInputModifiers.Shift | RawInputModifiers.Alt);
        Assert.True(vm.IsBrushAltHeld);
        Assert.False(vm.IsEffectiveBrushPaint);
        window.MouseDown(new(200, 100), MouseButton.Left, RawInputModifiers.Shift | RawInputModifiers.Alt);
        Assert.Equal("erase", vm.LiveBrushStroke!.Mode);
        window.MouseUp(new(200, 100), MouseButton.Left, RawInputModifiers.Shift | RawInputModifiers.Alt);
        await TestWaits.UntilAsync(() => vm.HistoryEntries[0].Label == "Erase stroke");
        var stroke = vm.SelectedLocal!.Strokes![1];
        Assert.Equal("erase", stroke.Mode);
        Assert.Equal(previousEnd, stroke.Points[0]);
        Assert.Equal(2, stroke.Points.Count);
        Assert.NotEqual(previousEnd, stroke.Points[1]);
        Assert.True(vm.IsBrushPaint);
        ShortcutUp(window, alt, RawInputModifiers.Shift);
        ShortcutUp(window, Key.LeftShift);
    }

    [AvaloniaTheory]
    [InlineData("focus")]
    [InlineData("deactivate")]
    [InlineData("leftalt")]
    [InlineData("rightalt")]
    [InlineData("stale")]
    public async Task BrushShortcutPointerMoveResynchronizesAltCursor(string transition)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareShortcutLocal(vm, catalog);
        vm.AddBrushCommand.Execute(null);
        vm.BrushMode = "paint";
        var overlay = new LocalsOverlayControl { DataContext = vm, Width = 640, Height = 480 };
        var window = new MainWindow { Focusable = true, Width = 640, Height = 480 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        window.Content = overlay;
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.Focus());
        window.MouseMove(new(100, 100));
        ShortcutDown(window, Key.LeftAlt, RawInputModifiers.Alt);
        Assert.True(vm.IsBrushAltHeld);
        switch (transition)
        {
            case "focus": window.FocusManager!.Focus(null); break;
            case "deactivate": typeof(WindowBase).GetMethod("HandleDeactivated",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null); break;
            case "leftalt":
            case "rightalt":
                ShortcutDown(window, Key.RightAlt, RawInputModifiers.Alt);
                ShortcutUp(window, transition == "leftalt" ? Key.LeftAlt : Key.RightAlt, RawInputModifiers.Alt);
                break;
        }
        var alt = transition != "stale";
        Assert.Equal(!alt, vm.IsBrushAltHeld);
        Assert.True(window.Focus());
        window.MouseMove(new(120, 100), alt ? RawInputModifiers.Alt : RawInputModifiers.None);
        Assert.Equal("None", overlay.Cursor?.ToString());
        Assert.True(overlay.BrushScreenRadius > 4);
        Assert.Equal(alt, vm.IsBrushAltHeld);
        Assert.Equal(!alt, vm.IsEffectiveBrushPaint);
        Assert.True(vm.IsBrushPaint);
        Assert.False(vm.IsBrushStrokeActive);
        ShortcutUp(window, Key.LeftAlt);
        ShortcutUp(window, Key.RightAlt);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrushShortcutPointerEnterResynchronizesAltCursor(bool alt)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareShortcutLocal(vm, catalog);
        vm.AddBrushCommand.Execute(null);
        vm.BrushMode = "paint";
        var overlay = new LocalsOverlayControl { DataContext = vm, Width = 640, Height = 480 };
        var window = new MainWindow { Focusable = true, Width = 640, Height = 480 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        window.Content = overlay;
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(new(-10, -10));
        vm.IsBrushAltHeld = !alt;
        var entered = false;
        overlay.PointerEntered += (_, _) =>
        {
            entered = true;
            Assert.Equal(alt, vm.IsBrushAltHeld);
            Assert.Equal(!alt, vm.IsEffectiveBrushPaint);
        };
        window.MouseMove(new(100, 100), alt ? RawInputModifiers.Alt : RawInputModifiers.None);
        Assert.True(entered);
        Assert.True(vm.IsBrushPaint);
    }

    [AvaloniaTheory]
    [InlineData("paint", true, false, "erase")]
    [InlineData("erase", true, false, "paint")]
    [InlineData("paint", false, true, "paint")]
    [InlineData("erase", false, true, "erase")]
    public async Task BrushShortcutStrokeUsesPressAltRatherThanHeldFlag(string preference, bool alt, bool stale, string expected)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await PrepareShortcutLocal(vm, catalog);
        vm.AddBrushCommand.Execute(null);
        vm.BrushMode = preference;
        var overlay = new LocalsOverlayControl { DataContext = vm, Width = 640, Height = 480 };
        var window = new MainWindow { Focusable = true, Width = 640, Height = 480 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        window.Content = overlay;
        Dispatcher.UIThread.RunJobs();
        vm.IsBrushAltHeld = stale;
        window.MouseDown(new(100, 100), MouseButton.Left, alt ? RawInputModifiers.Alt : RawInputModifiers.None);
        Assert.True(vm.IsBrushStrokeActive);
        Assert.Equal(expected, vm.LiveBrushStroke!.Mode);
        vm.IsBrushAltHeld = !stale;
        Assert.Equal(expected, vm.LiveBrushStroke.Mode);
        window.MouseUp(new(100, 100), MouseButton.Left);
        await TestWaits.UntilAsync(() => vm.HistoryEntries[0].Label == "Add Brush");
        Assert.Equal(expected, vm.SelectedLocal!.Strokes![0].Mode);
        Assert.Equal(preference, vm.BrushMode);
    }
}

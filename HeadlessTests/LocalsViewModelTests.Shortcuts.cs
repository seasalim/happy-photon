using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task ViewerShortcutsToggleLocalsWithToolArbitration()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        var before = await ShortcutSnapshot(vm, catalog);
        ShortcutPress(window, Key.O);
        ShortcutDown(window, Key.M);
        Assert.False(vm.ShowLocalMask);
        Assert.False(vm.IsLocalMaskHeld);
        ShortcutUp(window, Key.M);
        vm.ToggleWhiteBalancePickerCommand.Execute(null);
        Assert.True(vm.IsWhiteBalancePicking);
        ShortcutPress(window, Key.W, RawInputModifiers.Shift);
        await vm.ToggleLocalsModeCommand.ExecutionTask!;
        Assert.True(vm.IsLocalsMode);
        Assert.False(vm.IsWhiteBalancePicking);
        ShortcutPress(window, Key.W, RawInputModifiers.Shift);
        Assert.False(vm.IsLocalsMode);
        await vm.ToggleCropModeCommand.ExecuteAsync(null);
        Assert.True(vm.IsCropMode);
        ShortcutPress(window, Key.W, RawInputModifiers.Shift);
        await vm.ToggleLocalsModeCommand.ExecutionTask!;
        Assert.True(vm.IsLocalsMode);
        Assert.False(vm.IsCropMode);
        ShortcutPress(window, Key.W, RawInputModifiers.Shift);
        await AssertShortcutSnapshot(vm, catalog, before);
    }

    [AvaloniaTheory]
    [InlineData("browse")]
    [InlineData("export")]
    [InlineData("empty")]
    [InlineData("fullscreen")]
    [InlineData("interaction")]
    [InlineData("available")]
    public async Task ViewerShortcutsRespectToolAvailability(string unavailable)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        if (unavailable == "browse") vm.WorkspaceMode = WorkspaceMode.Browse;
        if (unavailable == "export") vm.WorkspaceMode = WorkspaceMode.Export;
        if (unavailable == "empty") vm.SelectedImage = null;
        if (unavailable == "fullscreen") vm.IsFullScreenMode = true;
        if (unavailable == "interaction") vm.StartupGateState = StartupGateState.Initializing;
        Assert.True(window.Focus());
        var pickerActivations = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsWhiteBalancePicking) && vm.IsWhiteBalancePicking)
                pickerActivations++;
        };
        if (unavailable == "available")
        {
            Assert.True(vm.ToggleWhiteBalancePickerCommand.CanExecute(null));
            ShortcutPress(window, Key.W);
            Assert.True(vm.IsWhiteBalancePicking);
            Assert.Equal(1, pickerActivations);
            ShortcutPress(window, Key.W);
            Assert.False(vm.IsWhiteBalancePicking);
            pickerActivations = 0;
        }
        ShortcutPress(window, Key.W, RawInputModifiers.Shift);
        if (unavailable == "available") await vm.ToggleLocalsModeCommand.ExecutionTask!;
        Assert.Equal(unavailable == "available", vm.IsLocalsMode);
        Assert.False(vm.IsWhiteBalancePicking);
        Assert.Equal(0, pickerActivations);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerMaskHoldRestoresPersistentToggleAndIgnoresRepeat(bool persistent)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await PrepareShortcutLocal(vm, catalog);
        vm.ShowLocalMask = persistent;
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        var before = await ShortcutSnapshot(vm, catalog);
        for (var i = 0; i < 3; i++)
        {
            ShortcutDown(window, Key.M);
            Assert.True(vm.IsLocalMaskHeld);
            Assert.True(vm.IsLocalMaskVisible);
            Assert.Equal(persistent, vm.ShowLocalMask);
        }
        ShortcutUp(window, Key.M);
        ShortcutUp(window, Key.M);
        Assert.False(vm.IsLocalMaskHeld);
        Assert.Equal(persistent, vm.ShowLocalMask);
        Assert.Equal(persistent, vm.IsLocalMaskVisible);
        ShortcutPress(window, Key.O);
        Assert.Equal(!persistent, vm.ShowLocalMask);
        Assert.Equal(!persistent, vm.IsLocalMaskVisible);
        await AssertShortcutSnapshot(vm, catalog, before);
    }

    [AvaloniaTheory]
    [InlineData("original")]
    [InlineData("split")]
    [InlineData("unavailable")]
    public async Task ViewerMaskShortcutsChangePreferenceUnderSuppression(string suppression)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await PrepareShortcutLocal(vm, catalog);
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        if (suppression == "original") await vm.ToggleBeforeAfterCommand.ExecuteAsync(null);
        if (suppression == "split") await vm.ToggleBeforeAfterSplitCommand.ExecuteAsync(null);
        if (suppression == "unavailable") vm.ApplyThumbnailLoadStatus(vm.SelectedImage!, ThumbnailLoadStatus.DeferredForHydration);
        Assert.True(window.Focus());
        var before = await ShortcutSnapshot(vm, catalog);
        ShortcutPress(window, Key.O);
        Assert.True(vm.ShowLocalMask);
        Assert.False(vm.IsLocalMaskVisible);
        ShortcutDown(window, Key.M);
        Assert.True(vm.IsLocalMaskHeld);
        Assert.False(vm.IsLocalMaskVisible);
        ShortcutUp(window, Key.M);
        if (suppression == "unavailable") vm.ApplyThumbnailLoadStatus(vm.SelectedImage!, ThumbnailLoadStatus.Loaded);
        else await vm.ResumeLocalEditingCommand.ExecuteAsync(null);
        Assert.True(vm.IsLocalMaskVisible);
        await AssertShortcutSnapshot(vm, catalog, before);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerMaskShortcutsYieldToPickers(bool hue)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await PrepareShortcutLocal(vm, catalog);
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        var before = await ShortcutSnapshot(vm, catalog);
        if (hue)
        {
            vm.ToggleLocalHuePickCommand.Execute(null);
            Assert.True(vm.IsLocalHuePicking);
        }
        else
        {
            // The tool row prevents global picking while Locals is open.
            vm.CloseLocalsCommand.Execute(null);
            vm.ToggleWhiteBalancePickerCommand.Execute(null);
            Assert.True(vm.IsWhiteBalancePicking);
        }
        var preference = vm.ShowLocalMask;
        ShortcutPress(window, Key.O);
        ShortcutDown(window, Key.M);
        Assert.Equal(preference, vm.ShowLocalMask);
        Assert.False(vm.IsLocalMaskHeld);
        Assert.Equal(hue, vm.IsLocalHuePicking);
        Assert.Equal(!hue, vm.IsWhiteBalancePicking);
        ShortcutUp(window, Key.M);
        if (hue) vm.ToggleLocalHuePickCommand.Execute(null);
        else vm.ToggleWhiteBalancePickerCommand.Execute(null);
        Assert.False(vm.ShowLocalMask);
        Assert.False(vm.IsLocalMaskVisible);
        await AssertShortcutSnapshot(vm, catalog, before);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ViewerMaskHoldClearsAcrossSessionExits(bool persistent)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await PrepareShortcutLocal(vm, catalog);
        vm.ShowLocalMask = persistent;
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var before = await ShortcutSnapshot(vm, catalog);
        foreach (var exit in new[] { "deactivate", "focus", "close", "shortcut", "workspace" })
        {
            Assert.True(window.Focus());
            ShortcutDown(window, Key.M);
            Assert.True(vm.IsLocalMaskHeld);
            Assert.True(vm.IsLocalMaskVisible);
            switch (exit)
            {
                case "deactivate": typeof(WindowBase).GetMethod("HandleDeactivated",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, null); break;
                case "focus": window.FocusManager!.Focus(null); break;
                case "close": vm.CloseLocalsCommand.Execute(null); break;
                case "shortcut": ShortcutPress(window, Key.W, RawInputModifiers.Shift); break;
                case "workspace": vm.WorkspaceMode = WorkspaceMode.Browse; break;
            }
            Assert.False(vm.IsLocalMaskHeld);
            Assert.Equal(persistent, vm.ShowLocalMask);
            vm.WorkspaceMode = WorkspaceMode.Develop;
            if (!vm.IsLocalsMode) await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
            Assert.Equal(persistent, vm.IsLocalMaskVisible);
            ShortcutUp(window, Key.M);
        }
        await AssertShortcutSnapshot(vm, catalog, before);
    }

    private async Task PrepareShortcutLocal(MainWindowViewModel vm, CatalogService catalog)
    {
        await Prepare(vm, catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
    }

    private static void ShortcutDown(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
    private static void ShortcutUp(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    private static void ShortcutPress(MainWindow window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        ShortcutDown(window, key, modifiers);
        ShortcutUp(window, key, modifiers);
    }
    private static async Task<(int History, string Saved, string Live)> ShortcutSnapshot(MainWindowViewModel vm, CatalogService catalog)
    {
        // Returning to Develop reloads history asynchronously.
        if (vm.PendingHistoryLoadTask is { } historyLoad)
            await historyLoad.WaitAsync(TestWaits.Condition);
        var image = vm.SelectedImage!;
        var saved = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single().EditSettings;
        return (vm.HistoryEntries.Count, RenderSettingsHash.Compute(saved), RenderSettingsHash.Compute(image.EditSettings));
    }
    private static async Task AssertShortcutSnapshot(MainWindowViewModel vm, CatalogService catalog,
        (int History, string Saved, string Live) before) => Assert.Equal(before, await ShortcutSnapshot(vm, catalog));
}

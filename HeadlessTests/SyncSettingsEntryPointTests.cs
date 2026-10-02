using Avalonia;
using Avalonia.Automation;
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

public sealed class SyncSettingsEntryPointTests
{
    [AvaloniaFact]
    public async Task EntryPointsShareCommandAndTrackEnablementAndTooltip()
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var browse = window.FindControl<BrowseGridView>("BrowseGridView")!;
        var actions = browse.FindControl<Button>("BrowseActionsButton")!;
        var flyout = Assert.IsType<MenuFlyout>(actions.Flyout);
        flyout.ShowAt(actions);
        Dispatcher.UIThread.RunJobs();
        var menuItem = flyout.Items.OfType<MenuItem>().Single(item => item.Name == "SyncSettingsMenuItem");
        var tile = Tile(window, photos[0]);
        tile.ContextMenu!.Open(tile);
        Dispatcher.UIThread.RunJobs();
        var contextItem = tile.ContextMenu.Items.OfType<MenuItem>()
            .Single(item => item.Name == "SyncSettingsContextMenuItem");
        var button = Named<Button>(window, "SyncSettingsButton");

        Assert.Equal("Sync settings", AutomationProperties.GetName(button));
        Assert.Equal("Sync settings…", button.Content);
        Assert.Contains("compact-button", button.Classes);
        Assert.True(ToolTip.GetShowOnDisabled(button));
        Assert.Same(vm.SyncSettingsCommand, button.Command);
        Assert.Same(button.Command, menuItem.Command);
        Assert.Same(button.Command, contextItem.Command);
        Assert.Equal("Ctrl+Shift+S", menuItem.InputGesture?.ToString());
        Assert.Equal(menuItem.InputGesture, contextItem.InputGesture);
        AssertState(true, "Sync settings from source.jpg to 1 photo (Ctrl+Shift+S)");

        vm.ToggleImageSelection(photos[1]);
        AssertState(false, "Select two or more photos to sync. The outlined photo is the source.");
        vm.ToggleImageSelection(photos[1]);
        AssertState(true, "Sync settings from source.jpg to 1 photo (Ctrl+Shift+S)");
        vm.EnterCompareCommand.Execute(null);
        AssertState(false, "Leave Compare to sync settings");
        Assert.True(button.IsEffectivelyVisible);

        void AssertState(bool enabled, string tip)
        {
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(enabled, vm.SyncSettingsCommand.CanExecute(null));
            Assert.Equal(enabled, button.IsEffectivelyEnabled);
            Assert.Equal(enabled, menuItem.IsEffectivelyEnabled);
            Assert.Equal(enabled, contextItem.IsEffectivelyEnabled);
            Assert.Equal(tip, vm.SyncSettingsToolTip);
            Assert.Equal(tip, ToolTip.GetTip(button));
        }
    }

    [AvaloniaTheory]
    [InlineData("grid", true)]
    [InlineData("loupe", true)]
    [InlineData("develop", false)]
    [InlineData("compare", false)]
    [InlineData("fullscreen", false)]
    [InlineData("export", false)]
    public async Task RealShortcutRunsOnlyInBrowseGridAndLoupe(string mode, bool expected)
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;

        switch (mode)
        {
            case "loupe":
                vm.EnterLoupeCommand.Execute(null);
                break;

            case "develop":
                vm.IsDevelopMode = true;
                break;

            case "compare":
                vm.EnterCompareCommand.Execute(null);
                break;

            case "fullscreen":
                vm.IsFullScreenMode = true;
                break;

            case "export":
                vm.WorkspaceMode = WorkspaceMode.Export;
                break;
        }

        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var shown = 0;
        vm.ShowPasteSettingsAsync = model =>
        {
            shown++;
            Assert.Equal(PasteSettingsMode.Sync, model.Mode);

            return Task.FromResult(true);
        };
        var binding = Assert.Single(window.KeyBindings,
            binding => binding.Gesture.ToString() == "Ctrl+Shift+S");
        Assert.Same(vm.SyncSettingsCommand, binding.Command);

        PressSync(window);
        await ObserveSyncAsync(vm);

        Assert.Equal(expected ? 1 : 0, shown);
        Assert.Equal(expected ? 1 : 0, photos[1].EditSettings.Exposure);
        Assert.Equal(1, photos[0].EditSettings.Exposure);
    }

    [AvaloniaFact]
    public async Task FocusedNumericEntryShadowsAnEnabledSyncShortcut()
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var pane = window.GetVisualDescendants().OfType<BrowseReviewPane>().Single();
        var grid = Assert.IsType<Grid>(Assert.IsType<Border>(pane.Content).Child);
        var entry = new NumericEntryBox { Text = "12", Width = 70 };
        Grid.SetRow(entry, 1);
        grid.Children.Add(entry);
        Dispatcher.UIThread.RunJobs();
        Assert.True(entry.Focus());
        Assert.True(entry.IsFocused);
        Assert.True(vm.SyncSettingsCommand.CanExecute(null));
        Assert.Contains(entry.KeyBindings, binding => binding.Gesture.ToString() == "Ctrl+Shift+S");
        var shown = 0;
        vm.ShowPasteSettingsAsync = _ =>
        {
            shown++;

            return Task.FromResult(true);
        };

        PressSync(window);
        await ObserveSyncAsync(vm);

        Assert.Equal(0, shown);
        Assert.Equal(0, photos[1].EditSettings.Exposure);
        Assert.True(vm.SyncSettingsCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task ThumbnailMenuSyncsFromRightClickedSelectedPhoto()
    {
        await using var fixture = new SyncTransferParityVm();
        var photos = await PrepareAsync(fixture);
        var vm = fixture.Vm;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var shown = 0;
        vm.ShowPasteSettingsAsync = model =>
        {
            shown++;
            Assert.Equal("From target.jpg to 1 photo", model.Summary);

            return Task.FromResult(true);
        };
        var tile = Tile(window, photos[1]);
        var point = tile.TranslatePoint(new Point(20, 20), window)!.Value;
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        Assert.True(tile.ContextMenu!.IsOpen);
        Assert.Same(photos[1], vm.SelectedImage);
        Assert.All(photos, photo => Assert.True(photo.IsSelected));
        var item = tile.ContextMenu.Items.OfType<MenuItem>()
            .Single(item => item.Name == "SyncSettingsContextMenuItem");
        Assert.Same(vm.SyncSettingsCommand, item.Command);
        Assert.True(item.IsEffectivelyEnabled);
        var popup = TopLevel.GetTopLevel(item)!;
        var menuPoint = item.TranslatePoint(new Point(20, item.Bounds.Height / 2), popup)!.Value;
        popup.MouseDown(menuPoint, MouseButton.Left);
        popup.MouseUp(menuPoint, MouseButton.Left);
        await ObserveSyncAsync(vm);

        Assert.Equal(1, shown);
        Assert.All(photos, photo => Assert.Equal(0, photo.EditSettings.Exposure));
        Assert.Empty((await fixture.Catalog.LoadEditHistoryAsync(photos[1].CatalogId)).Entries);
        Assert.Single((await fixture.Catalog.LoadEditHistoryAsync(photos[0].CatalogId)).Entries,
            entry => entry.Label == "Paste settings");
        var stored = await fixture.Catalog.LoadImageStatesAsync(photos.Select(photo => photo.FilePath).ToArray());
        Assert.All(stored.Values, versions => Assert.Equal(0, Assert.Single(versions).EditSettings.Exposure));
    }

    private static async Task<ImageFile[]> PrepareAsync(SyncTransferParityVm fixture)
    {
        await fixture.InitializeAsync();
        var source = await fixture.ImageAsync("source", new() { Exposure = 1 });
        var target = await fixture.ImageAsync("target", new());
        var vm = fixture.Vm;
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = false;
        vm.Browse.SetImages([source, target]);
        vm.SelectedImage = source;
        vm.SelectAllCommand.Execute(null);
        Assert.True(vm.SyncSettingsCommand.CanExecute(null));

        return [source, target];
    }

    private static async Task ObserveSyncAsync(MainWindowViewModel vm)
    {
        if (vm.SyncSettingsCommand.ExecutionTask is { } pending)
        {
            await pending.WaitAsync(TestWaits.Condition);
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static void PressSync(Window window)
    {
        window.KeyPress(Key.S, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
        window.KeyRelease(Key.S, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Named<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static Border Tile(Window window, ImageFile photo) =>
        window.GetVisualDescendants().OfType<Border>()
            .Single(control => control.Name == "ThumbnailTile" && ReferenceEquals(control.DataContext, photo));
}

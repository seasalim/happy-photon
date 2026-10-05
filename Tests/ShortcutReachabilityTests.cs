using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ShortcutReachabilityTests
{
    [AvaloniaFact]
    public async Task Claims_ResolveToInteractiveControlsInTheirWorkspaces()
    {
        using var catalog = new CatalogService(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-reachability-{Guid.NewGuid():N}"));
        await catalog.InitializeAsync();
        var firstSettings = new EditSettings { Exposure = 1 };
        var firstPath = Path.Combine(catalog.CatalogPath, "first.jpg");
        var firstId = await catalog.GetOrCreateImageAsync(firstPath);
        await catalog.SaveEditSettingsWithHistoryAsync(
            firstId,
            firstSettings,
            new CatalogEditHistoryMutation(-1,
            [
                new(0, "Original", new EditSettings()),
                new(1, "Exposure +1.00", firstSettings)
            ], 1));
        using var source = new ImageMagick.MagickImage(ImageMagick.MagickColors.Gray, 160, 100);
        source.Write(firstPath);
        await using var vm = new MainWindowViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = new[]
        {
            new ImageFile(firstPath)
            {
                CatalogId = firstId,
                EditSettings = firstSettings
            },
            new ImageFile(Path.Combine(catalog.CatalogPath, "second.jpg"))
        };
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        vm.ToggleImageSelection(images[0]);
        var window = new MainWindow();
        using var windowScope = TestUiScope.ForMainWindow(window, vm);
        Dispatcher.UIThread.RunJobs();

        try
        {
            foreach (var entry in ShortcutCatalog.Groups.SelectMany(group => group.Entries))
            {
                Assert.True(HasValidReachability(entry),
                    $"{entry.Keys} has an invalid reachability declaration.");
                foreach (var claim in entry.Reachability.Where(claim => claim.ControlName != null))
                {
                    var control = await ResolveClaimControl(window, vm, claim);
                    Assert.True(
                        control != null,
                        $"{entry.Keys} target {claim.ControlName} was not found in {claim.Workspace}.");
                    Assert.True(IsValidControlTarget(control!),
                        $"{entry.Keys} target {claim.ControlName} is a container, not an interactive control.");
                    Assert.True(control!.IsEffectivelyVisible,
                        $"{entry.Keys} target {claim.ControlName} is not visible in {claim.Workspace}.");
                    Assert.All(
                        control.GetVisualAncestors().OfType<Control>(),
                        ancestor => Assert.True(ancestor.IsEffectivelyEnabled,
                            $"{entry.Keys} target {claim.ControlName} has a disabled ancestor."));
                }
            }
        }
        finally
        {
            windowScope.Dispose();
        }
    }

    [AvaloniaFact]
    public void Claims_RejectMissingAmbiguousAndContainerDeclarations()
    {
        var representationSwitch = ShortcutCatalog.Groups
            .SelectMany(group => group.Entries)
            .Single(entry => entry.Keys == "Shift+R");
        var missing = new ShortcutEntry("K", "Missing", []);
        var ambiguous = new ShortcutEntry("K", "Ambiguous",
        [
            new ShortcutReachabilityClaim(
                "Ambiguous",
                "BrowseTabButton",
                ShortcutWorkspace.Browse,
                ShortcutExemption.DialogAffordance)
        ]);

        Assert.Contains(representationSwitch.Reachability, claim =>
            claim.Workspace == ShortcutWorkspace.Develop &&
            claim.ControlName == "RawJpegSwitchButton");
        Assert.False(HasValidReachability(missing));
        Assert.False(HasValidReachability(ambiguous));
        Assert.True(IsValidControlTarget(new CompactSlider()));
        Assert.False(IsValidControlTarget(new UserControl()));
        Assert.False(IsValidControlTarget(new StackPanel()));
    }

    [Theory]
    [InlineData("Click value", "SaturationSlider")]
    [InlineData("Tab / Shift+Tab", "SaturationSlider")]
    [InlineData("A", "VisualizeSpotsButton")]
    [InlineData("[  /  ]", "SpotSizeSlider")]
    [InlineData("Shift+[  /  Shift+]", "SpotFeatherSlider")]
    [InlineData("Shift+W", "LocalsModeButton")]
    [InlineData("O", "ShowLocalMaskButton")]
    [InlineData("Hold M", "ShowLocalMaskButton")]
    [InlineData("B", "AddBrushButton")]
    [InlineData("[  /  ]", "BrushSizeSlider")]
    [InlineData("Shift+[  /  Shift+]", "BrushFeatherSlider")]
    [InlineData("Hold Alt", "BrushPaintButton")]
    [InlineData("Hold Alt", "BrushEraseButton")]
    public void LocalsShortcutsDeclareDevelopControls(string keys, string control)
    {
        var entry = Assert.Single(ShortcutCatalog.Groups.Single(group => group.Title == "Develop and edit")
            .Entries, entry => entry.Keys == keys);
        Assert.Contains(entry.Reachability, claim => claim.ControlName == control &&
            claim.Workspace == ShortcutWorkspace.Develop);
    }

    [Fact]
    public void SyncShortcutSitsBesidePasteAndClaimsGridAndLoupeFooter()
    {
        var entries = ShortcutCatalog.Groups.Single(group => group.Title == "Develop and edit").Entries;
        var pasteIndex = entries.ToList().FindIndex(entry => entry.Keys == "Ctrl+Shift+V");
        var sync = entries[pasteIndex + 1];

        Assert.Equal("Ctrl+Shift+S", sync.Keys);
        Assert.Equal("Sync settings from the active photo to the rest of the Browse selection", sync.Action);
        Assert.Equal(new ShortcutWorkspace?[] { ShortcutWorkspace.Browse, ShortcutWorkspace.Loupe },
            sync.Reachability.Select(claim => claim.Workspace));
        Assert.All(sync.Reachability, claim => Assert.Equal("SyncSettingsButton", claim.ControlName));
    }

    [Fact]
    public void UndoClaimsBrowseStatusActionAndDevelopUndo()
    {
        var undo = Assert.Single(ShortcutCatalog.Groups.SelectMany(group => group.Entries), entry => entry.Keys == "Ctrl+Z");
        Assert.Equal(new ShortcutWorkspace?[] { ShortcutWorkspace.Develop, ShortcutWorkspace.Browse },
            undo.Reachability.Select(claim => claim.Workspace));
        Assert.Equal(new[] { "UndoEditButton", "UndoSyncButton" }, undo.Reachability.Select(claim => claim.ControlName));
    }

    private static bool HasValidReachability(ShortcutEntry entry) =>
        entry.Reachability.Count > 0 && entry.Reachability.All(claim =>
        {
            var declaresTarget = claim.ControlName != null || claim.Workspace != null;
            var hasExemption = claim.Exemption != null;
            return !string.IsNullOrWhiteSpace(claim.Action) &&
                   declaresTarget != hasExemption &&
                   (!declaresTarget ||
                    !string.IsNullOrWhiteSpace(claim.ControlName) &&
                    claim.Workspace != null);
        });

    private static bool IsValidControlTarget(Control control) =>
        control is CompactSlider || control is not UserControl and not Panel;

    private static async Task<Control?> ResolveClaimControl(
        MainWindow window,
        MainWindowViewModel vm,
        ShortcutReachabilityClaim claim)
    {
        vm.ExitCompareCommand.Execute(null);
        vm.ExitLoupeCommand.Execute(null);
        vm.IsFullScreenMode = false;
        vm.IsCropMode = false;
        vm.CloseLocalsCommand.Execute(null);
        vm.CloseSpotsCommand.Execute(null);
        vm.WorkspaceMode = claim.Workspace switch
        {
            ShortcutWorkspace.Develop => WorkspaceMode.Develop,
            ShortcutWorkspace.Export => WorkspaceMode.Export,
            _ => WorkspaceMode.Browse
        };
        if (claim.Workspace == ShortcutWorkspace.Compare)
        {
            if (vm.Browse.SelectedCount < 2)
            {
                foreach (var image in vm.Browse.VisibleImages.Take(2))
                {
                    if (!image.IsSelected) vm.ToggleImageSelection(image);
                }
            }
            vm.EnterCompareCommand.Execute(null);
        }
        if (claim.Workspace == ShortcutWorkspace.Loupe)
        {
            vm.EnterLoupeCommand.Execute(null);
        }
        vm.IsFullScreenMode = claim.Workspace == ShortcutWorkspace.FullScreen;
        if (claim.ControlName is "ApplyCropButton" or "CancelCropButton" or "CropRatioPicker")
        {
            vm.IsCropMode = true;
        }
        if (claim.ControlName is "ShowLocalMaskButton" or "AddBrushButton" or "BrushSizeSlider" or
            "BrushFeatherSlider" or "BrushPaintButton" or "BrushEraseButton")
        {
            await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
            if (claim.ControlName != "ShowLocalMaskButton") vm.AddBrushCommand.Execute(null);
        }
        if (claim.ControlName is "VisualizeSpotsButton" or "SpotSizeSlider" or "SpotFeatherSlider")
        {
            await TestWaits.UntilAsync(() => vm.PreviewImage != null);
            await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        }

        if (claim.ControlName == "UndoSyncButton")
        {
            vm.SelectAllCommand.Execute(null);
            vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);
            await vm.SyncSettingsCommand.ExecuteAsync(null);
            Assert.True(vm.UndoBatchCommand.CanExecute(null));
        }

        Dispatcher.UIThread.RunJobs();
        if (claim.Workspace == ShortcutWorkspace.FullScreen)
        {
            window.MouseMove(new Avalonia.Point(20, 20),
                Avalonia.Input.RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        if (claim.ControlName is "SelectAllMenuItem" or "DeselectAllMenuItem")
        {
            var button = window.FindControl<BrowseGridView>("BrowseGridView")!
                .FindControl<Button>("BrowseActionsButton")!;
            var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            return flyout.Items.OfType<Control>()
                .FirstOrDefault(control => control.Name == claim.ControlName);
        }

        if (claim.ControlName is "DeleteImageMenuItem" or "NewVersionMenuItem")
        {
            var tile = window.GetVisualDescendants().OfType<Border>()
                .First(control => control.Name == "ThumbnailTile");
            tile.ContextMenu!.Open(tile);
            Dispatcher.UIThread.RunJobs();
            return tile.ContextMenu.Items.OfType<Control>()
                .FirstOrDefault(control => control.Name == claim.ControlName);
        }

        if (claim.ControlName == "ClearHistoryAboveStepMenuItem")
        {
            await TestWaits.UntilAsync(() =>
                vm.IsHistoryLoaded && vm.HistoryEntries.Count > 1);
            Dispatcher.UIThread.RunJobs();
            var row = window.GetVisualDescendants().OfType<Button>()
                .First(control => control.Classes.Contains("history-row") &&
                                  control.DataContext is EditHistoryEntry entry &&
                                  entry.Sequence == 0);
            row.ContextMenu!.Open(row);
            Dispatcher.UIThread.RunJobs();
            return row.ContextMenu.Items.OfType<Control>()
                .FirstOrDefault(control => control.Name == claim.ControlName);
        }

        if (claim.ControlName == "ExpanderHeader")
        {
            return window.GetVisualDescendants().OfType<DevelopGroup>().First()
                .GetVisualDescendants().OfType<Control>()
                .Single(control => control.Name == claim.ControlName);
        }

        return window.GetVisualDescendants().Prepend(window)
            .OfType<Control>()
            .FirstOrDefault(control =>
                control.Name == claim.ControlName &&
                control.IsEffectivelyVisible);
    }
}

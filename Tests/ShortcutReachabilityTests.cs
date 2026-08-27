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
        await using var vm = new MainWindowViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = new[]
        {
            new ImageFile(Path.Combine(catalog.CatalogPath, "first.jpg")),
            new ImageFile(Path.Combine(catalog.CatalogPath, "second.jpg"))
        };
        vm.Browse.SetImages(images);
        vm.SelectedImage = images[0];
        vm.ToggleImageSelection(images[0]);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            foreach (var entry in ShortcutCatalog.Groups.SelectMany(group => group.Entries))
            {
                Assert.True(HasValidReachability(entry),
                    $"{entry.Keys} has an invalid reachability declaration.");
                foreach (var claim in entry.Reachability.Where(claim => claim.ControlName != null))
                {
                    var control = ResolveClaimControl(window, vm, claim);
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
            window.DataContext = null;
            window.Close();
        }
    }

    [Fact]
    public void Claims_RejectMissingAmbiguousAndContainerDeclarations()
    {
        var missing = new ShortcutEntry("K", "Missing", []);
        var ambiguous = new ShortcutEntry("K", "Ambiguous",
        [
            new ShortcutReachabilityClaim(
                "Ambiguous",
                "BrowseTabButton",
                ShortcutWorkspace.Browse,
                ShortcutExemption.DialogAffordance)
        ]);

        Assert.False(HasValidReachability(missing));
        Assert.False(HasValidReachability(ambiguous));
        Assert.False(IsValidControlTarget(new UserControl()));
        Assert.False(IsValidControlTarget(new StackPanel()));
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
        control is not UserControl and not Panel;

    private static Control? ResolveClaimControl(
        MainWindow window,
        MainWindowViewModel vm,
        ShortcutReachabilityClaim claim)
    {
        vm.ExitCompareCommand.Execute(null);
        vm.IsFullScreenMode = false;
        vm.IsCropMode = false;
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
        vm.IsFullScreenMode = claim.Workspace == ShortcutWorkspace.FullScreen;
        if (claim.ControlName is "ApplyCropButton" or "CancelCropButton")
        {
            vm.IsCropMode = true;
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

        return window.GetVisualDescendants().Prepend(window)
            .OfType<Control>()
            .FirstOrDefault(control =>
                control.Name == claim.ControlName &&
                control.IsEffectivelyVisible);
    }
}

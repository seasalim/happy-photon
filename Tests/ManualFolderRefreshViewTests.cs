using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ManualFolderRefreshViewTests
{
    [AvaloniaFact]
    public void FolderPanel_MoreActionsPreserveEventsAndAutomationNames()
    {
        var panel = new FolderTreePanel();
        var button = panel.FindControl<Button>("FolderActionsButton")!;
        var flyout = Assert.IsType<MenuFlyout>(button.Flyout);
        var items = flyout.Items.OfType<MenuItem>().ToArray();
        var importRequested = 0;
        var changeRequested = 0;
        panel.ImportCatalogRequested += (_, _) => importRequested++;
        panel.ChangeFolderRequested += (_, _) => changeRequested++;

        Assert.Equal("⋯", button.Content);
        Assert.Equal("More folder actions", ToolTip.GetTip(button));
        Assert.Equal("More folder actions", AutomationProperties.GetName(button));
        Assert.Equal(
            ["Import from Lightroom…", "Change browsing location…"],
            items.Select(item => item.Header));
        Assert.Equal(
            ["Import from Lightroom catalog", "Change browsing location"],
            items.Select(AutomationProperties.GetName));
        Assert.Equal(
            [
                "Import ratings, flags, and color labels from Lightroom Classic.",
                "Choose the top-level location shown in the folder tree."
            ],
            items.Select(ToolTip.GetTip));

        items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal(1, importRequested);
        Assert.Equal(1, changeRequested);
    }

    [AvaloniaFact]
    public void FolderPanel_RefreshButtonUsesVectorMetadataAndRaisesEvent()
    {
        var panel = new FolderTreePanel();
        var button = panel.FindControl<Button>("RefreshFolderButton")!;
        var requested = 0;
        panel.RefreshFolderRequested += (_, _) => requested++;

        Assert.False(panel.CanRefreshFolder);
        panel.SelectedFolder = FolderNode.CreateDummy();
        Assert.False(panel.CanRefreshFolder);
        panel.SelectedFolder = new FolderNode(Path.GetTempPath());

        Assert.True(panel.CanRefreshFolder);
        var icon = Assert.IsType<PathIcon>(button.Content);
        Assert.IsType<PathGeometry>(icon.Data);
        Assert.Equal(
            "Refresh folder — re-read photographs in the current folder.",
            ToolTip.GetTip(button));
        Assert.Equal(
            "Refresh current folder",
            AutomationProperties.GetName(button));

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(1, requested);
    }

    [Fact]
    public void RefreshScroll_IsBackgroundPostedAndGenerationGuarded()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-refresh-view-{Guid.NewGuid():N}");
        var photos = Path.Combine(root, "photos");
        Directory.CreateDirectory(photos);
        TestImages.WriteJpeg(Path.Combine(photos, "image.jpg"));
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        Complete(catalog.InitializeAsync());
        var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.SetRootFolder(photos);
        var generation = Complete(vm.RefreshCurrentFolderAsync());
        Action? callback = null;
        DispatcherPriority? priority = null;
        var scrollCount = 0;

        MainWindow.PostRefreshScroll(
            vm,
            generation,
            () => true,
            () => scrollCount++,
            (action, postedPriority) =>
            {
                callback = action;
                priority = postedPriority;
            });

        Assert.Equal(DispatcherPriority.Background, priority);
        Assert.NotNull(callback);
        callback!();
        Assert.Equal(1, scrollCount);

        Complete(vm.LoadFolderAsync(photos));
        callback();
        Assert.Equal(1, scrollCount);

        Complete(vm.DisposeAsync().AsTask());
        catalog.Dispose();
        Directory.Delete(root, recursive: true);
    }

    private static void Complete(Task task) => task.GetAwaiter().GetResult();

    private static T Complete<T>(Task<T> task) =>
        task.GetAwaiter().GetResult();
}

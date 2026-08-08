using Avalonia.Automation;
using Avalonia.Controls;
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

[Collection(AvaloniaTestCollection.Name)]
public sealed class ManualFolderRefreshViewTests
{
    private readonly AvaloniaTestFixture _fixture;

    public ManualFolderRefreshViewTests(AvaloniaTestFixture fixture) =>
        _fixture = fixture;

    [WindowsFact]
    public void FolderPanel_RefreshButtonUsesVectorMetadataAndRaisesEvent()
    {
        _fixture.RequireWindows();
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
        using (var image = new MagickImage(MagickColors.Gray, 16, 16))
        {
            image.Write(
                Path.Combine(photos, "image.jpg"),
                MagickFormat.Jpeg);
        }
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        Complete(catalog.InitializeAsync());
        var vm = new MainWindowViewModel(catalog);
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

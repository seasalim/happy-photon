using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FolderTreeLayoutTests
{
    [AvaloniaFact]
    public async Task ClearingPhotoSelectionKeepsFolderTreeStationary()
    {
        using var temporary = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(
            temporary.Path,
            "catalog"));
        await using var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var browseRoot = Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "photos")).FullName;
        Directory.CreateDirectory(Path.Combine(browseRoot, "child"));
        viewModel.SetRootFolder(browseRoot, selectRoot: false);
        var image = new ImageFile(Path.Combine(browseRoot, "image.jpg"));
        viewModel.Browse.SetImages([image]);
        viewModel.SelectedImage = image;
        var window = new MainWindow
        {
            Width = 1200,
            Height = 800,
            DataContext = viewModel
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var folderTree = window.FindControl<FolderTreePanel>(
                "FolderTreePanel")!;
            var navigator = window.FindControl<Border>("NavigatorPanel")!;
            var navigatorPreview = window.FindControl<Border>("NavigatorPreviewFrame")!;
            var leftPanel = window.FindControl<Border>("TourLeftPanel")!;
            var tree = folderTree.FindControl<TreeView>("FolderTree")!;
            var item = tree.GetVisualDescendants()
                .OfType<TreeViewItem>()
                .Single(candidate => ReferenceEquals(
                    candidate.DataContext,
                    viewModel.RootFolders[0]));
            var row = Assert.Single(
                item.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("folder-row") &&
                          ReferenceEquals(
                              border.DataContext,
                              viewModel.RootFolders[0]));
            var layoutRoot = Assert.Single(
                item.GetVisualDescendants().OfType<Border>(),
                border => border.Name == "PART_LayoutRoot" &&
                          ReferenceEquals(border.TemplatedParent, item));
            var chevron = Assert.Single(
                item.GetVisualDescendants().OfType<ToggleButton>(),
                button => button.Name == "PART_ExpandCollapseChevron" &&
                          ReferenceEquals(button.TemplatedParent, item));
            var selectedTreeBounds = folderTree.Bounds;
            var selectedNavigatorBounds = navigator.Bounds;

            Assert.Equal(240, leftPanel.Bounds.Width);
            Assert.Equal(168, navigatorPreview.Bounds.Height);
            Assert.Equal(24, layoutRoot.Bounds.Height);
            Assert.True(row.Bounds.Height >= 20);
            Assert.True(
                chevron.Bounds.Width >= 20,
                $"Chevron width was {chevron.Bounds.Width}.");
            Assert.True(
                chevron.Bounds.Height >= 20,
                $"Chevron height was {chevron.Bounds.Height}.");

            viewModel.SelectedImage = null;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();

            Assert.True(navigator.IsVisible);
            Assert.Equal(selectedNavigatorBounds, navigator.Bounds);
            Assert.Equal(selectedTreeBounds, folderTree.Bounds);
        }
        finally
        {
            window.DataContext = null;
            window.Close();
        }
    }
}

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace HappyPhoton.Tests;

public sealed class FolderTreeLayoutTests
{
    [AvaloniaFact]
    public async Task TallFolderTreeRevealsThinOverlayScrollBar()
    {
        var (window, tree) = ShowFolderTree(childCount: 40);

        try
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            var scrollViewer = Assert.Single(
                tree.GetVisualDescendants().OfType<ScrollViewer>());
            var scrollBar = Assert.Single(
                scrollViewer.GetVisualDescendants().OfType<ScrollBar>(),
                candidate => candidate.Orientation == Avalonia.Layout.Orientation.Vertical);
            var thumb = Assert.Single(
                scrollBar.GetVisualDescendants().OfType<Thumb>());

            Assert.True(scrollBar.IsVisible);
            Assert.Equal(0, thumb.Opacity);
            Assert.Equal(
                scrollBar.TranslatePoint(default, window)!.Value.Y,
                thumb.TranslatePoint(default, window)!.Value.Y);

            var hoverPoint = tree.TranslatePoint(
                new Point(tree.Bounds.Width / 2, tree.Bounds.Height / 2),
                window)!.Value;
            window.MouseMove(hoverPoint, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            await TestWaits.UntilAsync(() => thumb.Opacity == 1);

            Assert.Equal(1, thumb.Opacity);
            Assert.Equal(6, thumb.Bounds.Width);
            Assert.True(thumb.Bounds.Height > 0);
            var thumbPoint = thumb.TranslatePoint(
                new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2),
                window)!.Value;
            window.MouseMove(thumbPoint, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            await TestWaits.UntilAsync(() => scrollBar.IsExpanded);

            Dispatcher.UIThread.RunJobs();

            Assert.True(scrollBar.IsExpanded);
            Assert.Equal(12, scrollBar.Bounds.Width);
            Assert.Equal(10, thumb.Bounds.Width);
            Assert.Equal(1, thumb.Opacity);
            Assert.Null(Assert.Single(
                scrollBar.GetVisualDescendants().OfType<Grid>(),
                grid => grid.Name == "Root").Background);
            Assert.Equal(0, Assert.Single(
                scrollBar.GetVisualDescendants().OfType<Rectangle>(),
                rectangle => rectangle.Name == "TrackRect").Opacity);
            var lineButtons = scrollBar.GetVisualDescendants().OfType<RepeatButton>()
                .Where(button => button.Name is
                    "PART_LineUpButton" or "PART_LineDownButton")
                .ToArray();
            Assert.Equal(2, lineButtons.Length);
            Assert.All(
                lineButtons,
                button => Assert.False(button.IsVisible));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShortFolderTreeDoesNotShowScrollBar()
    {
        var (window, tree) = ShowFolderTree(childCount: 2);

        try
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var scrollViewer = Assert.Single(
                tree.GetVisualDescendants().OfType<ScrollViewer>());
            var scrollBar = Assert.Single(
                scrollViewer.GetVisualDescendants().OfType<ScrollBar>(),
                candidate => candidate.Orientation == Avalonia.Layout.Orientation.Vertical);

            Assert.False(scrollBar.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DraggingFolderTreeThumbScrollsContent()
    {
        var (window, tree) = ShowFolderTree(childCount: 40);

        try
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var scrollViewer = Assert.Single(
                tree.GetVisualDescendants().OfType<ScrollViewer>());
            var scrollBar = Assert.Single(
                scrollViewer.GetVisualDescendants().OfType<ScrollBar>(),
                candidate => candidate.Orientation == Avalonia.Layout.Orientation.Vertical);
            var thumb = Assert.Single(
                scrollBar.GetVisualDescendants().OfType<Thumb>());
            var start = thumb.TranslatePoint(
                new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2),
                window)!.Value;
            var end = start + new Vector(0, 80);

            window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();

            var thumbBorder = Assert.Single(
                thumb.GetVisualDescendants().OfType<Border>());
            Assert.True(Application.Current!.TryGetResource(
                "TextSecondary", window.ActualThemeVariant, out var pressedBrush));
            Assert.Equal(
                Assert.IsAssignableFrom<ISolidColorBrush>(pressedBrush).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(thumbBorder.Background).Color);

            window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(scrollViewer.Offset.Y > 0);
        }
        finally
        {
            window.Close();
        }
    }

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

    private static (Window Window, TreeView Tree) ShowFolderTree(int childCount)
    {
        var root = new FolderNode("root") { IsExpanded = true };
        for (var index = 1; index <= childCount; index++)
        {
            root.Children.Add(new FolderNode($"folder-{index:00}"));
        }

        var panel = new FolderTreePanel
        {
            RootFolders = new ObservableCollection<FolderNode> { root }
        };
        var window = new Window
        {
            Width = 240,
            Height = 300,
            Content = panel
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (window, panel.FindControl<TreeView>("FolderTree")!);
    }
}

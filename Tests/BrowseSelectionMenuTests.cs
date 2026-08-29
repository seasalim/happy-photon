using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace HappyPhoton.Tests;

public sealed class BrowseSelectionMenuTests
{
    [AvaloniaFact]
    public async Task UnflagAndBadgeControls_HandleMixedSelectionWithoutTileReselection()
    {
        using var catalog = new CatalogService(NewRoot());
        await catalog.InitializeAsync();
        await using var vm = NewViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = new[]
        {
            new ImageFile(Path.Combine(Path.GetTempPath(), "picked.jpg"))
                { Flag = ImageFlag.Picked },
            new ImageFile(Path.Combine(Path.GetTempPath(), "rejected.jpg"))
                { Flag = ImageFlag.Rejected },
            new ImageFile(Path.Combine(Path.GetTempPath(), "active.jpg"))
        };
        vm.Browse.SetImages(images);
        vm.ToggleImageSelection(images[0]);
        vm.ToggleImageSelection(images[1]);
        vm.SelectedImage = images[2];
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var unflag = window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "UnflagImageButton");

        Assert.Same(vm.UnpickImageCommand, unflag.Command);
        await vm.UnpickImageCommand.ExecuteAsync(null);
        Assert.Equal(ImageFlag.Unflagged, images[0].Flag);
        Assert.Equal(ImageFlag.Unflagged, images[1].Flag);
        Dispatcher.UIThread.RunJobs();

        var badge = window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "SelectionBadgeButton" &&
                              ReferenceEquals(button.DataContext, images[1]));
        var unselectedBadge = window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "SelectionBadgeButton" &&
                              ReferenceEquals(button.DataContext, images[2]));
        Assert.Equal(0, unselectedBadge.Opacity);
        // An invisible badge must not become a tab stop in the Browse grid.
        Assert.False(unselectedBadge.Focusable);
        var unselectedTile = window.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "ThumbnailTile" &&
                              ReferenceEquals(border.DataContext, images[2]));
        var hoverPoint = unselectedTile.TranslatePoint(new Point(20, 20), window)!.Value;
        window.MouseMove(hoverPoint, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(unselectedBadge.Opacity > 0);
        var point = badge.TranslatePoint(new Point(8, 8), window)!.Value;
        var badgeTile = window.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "ThumbnailTile" &&
                              ReferenceEquals(border.DataContext, images[1]));
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(images[0].IsSelected);
        Assert.False(images[1].IsSelected);
        Assert.False(images[2].IsSelected);
        Assert.Same(images[2], vm.SelectedImage);

        window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(badgeTile.ContextMenu!.IsOpen);

        window.DataContext = null;
        window.Close();
    }

    [AvaloniaFact]
    public void ThumbnailContextMenu_HasFileOperationsAndRaisesRequests()
    {
        var image = new ImageFile(Path.Combine(Path.GetTempPath(), "menu.jpg"))
        {
            CatalogId = 2,
            Version = 2,
            VersionCount = 2
        };
        var control = new BrowseGridView
        {
            Images = new ObservableCollection<ImageFile> { image }
        };
        var window = new Window { Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var tile = Assert.Single(control.GetVisualDescendants().OfType<Border>(),
            border => ReferenceEquals(border.DataContext, image) &&
                      border.Classes.Contains("thumbnail"));
        var menu = tile.ContextMenu!;
        var items = menu.Items.ToArray();
        var copy = Assert.IsType<MenuItem>(items[0]);
        var reveal = Assert.IsType<MenuItem>(items[1]);
        Assert.IsType<Separator>(items[2]);
        var createVersion = Assert.IsType<MenuItem>(items[3]);
        var renameVersion = Assert.IsType<MenuItem>(items[4]);
        var deleteVersion = Assert.IsType<MenuItem>(items[5]);
        Assert.IsType<Separator>(items[6]);
        var delete = Assert.IsType<MenuItem>(items[7]);
        Assert.Equal(
            ["Copy path", "Reveal in File Explorer", "New Version from Current",
                "Rename version label…", "Delete version", "Delete selection…"],
            new[] { copy, reveal, createVersion, renameVersion, deleteVersion, delete }
                .Select(item => item.Header));
        Assert.All(new[]
            {
                copy, reveal, createVersion, renameVersion, deleteVersion, delete
            }, item => Assert.True(item.IsEnabled));
        var requests = new int[6];
        control.CopyImagePathsRequested += (_, _) => requests[0]++;
        control.RevealImageRequested += (_, _) => requests[1]++;
        control.NewVersionRequested += (_, target) =>
        {
            Assert.Same(image, target);
            requests[2]++;
        };
        control.RenameVersionRequested += (_, target) =>
        {
            Assert.Same(image, target);
            requests[3]++;
        };
        control.DeleteVersionRequested += (_, target) =>
        {
            Assert.Same(image, target);
            requests[4]++;
        };
        control.DeleteImagesRequested += (_, _) => requests[5]++;

        menu.PlacementTarget = tile;
        menu.Open();
        Dispatcher.UIThread.RunJobs();
        copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        reveal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        createVersion.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        renameVersion.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        deleteVersion.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal([1, 1, 1, 1, 1, 1], requests);
        window.Close();
    }

    [AvaloniaFact]
    public void VersionContextMenu_FolderSwitchCannotTargetPreviousFolder()
    {
        var previous = new ImageFile(Path.Combine(Path.GetTempPath(), "previous.jpg"));
        var current = new ImageFile(Path.Combine(Path.GetTempPath(), "current.jpg"));
        var control = new BrowseGridView
        {
            Images = new ObservableCollection<ImageFile> { previous }
        };
        var window = new Window { Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var tile = Assert.Single(control.GetVisualDescendants().OfType<Border>(),
            border => ReferenceEquals(border.DataContext, previous) &&
                      border.Classes.Contains("thumbnail"));
        var menu = tile.ContextMenu!;
        var createVersion = Assert.IsType<MenuItem>(menu.Items.ElementAt(3));
        control.ApplyRightClickSelection(previous);
        menu.PlacementTarget = tile;
        menu.Open();
        Dispatcher.UIThread.RunJobs();

        control.Images = new ObservableCollection<ImageFile> { current };
        control.SelectedImage = current;
        ImageFile? requested = null;
        control.NewVersionRequested += (_, target) => requested = target;

        createVersion.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Same(current, requested);

        requested = null;
        control.Images = [];
        control.SelectedImage = null;
        createVersion.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Null(requested);
        window.Close();
    }

    [AvaloniaFact]
    public async Task DeleteVersionContextMenu_UsesConfirmationAndCancelKeepsVersion()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(Path.Combine(root, "catalog"));
        await catalog.InitializeAsync();
        var path = Path.Combine(root, "versioned.jpg");
        await File.WriteAllBytesAsync(path, [1]);
        var primaryId = await catalog.GetOrCreateImageAsync(path);
        var secondState = (await catalog.CreateVersionAsync(primaryId))!;
        var primary = new ImageFile(path)
        {
            CatalogId = primaryId,
            VersionCount = 2
        };
        var second = new ImageFile(path)
        {
            CatalogId = secondState.CatalogId,
            Version = secondState.Version,
            VersionCount = 2
        };
        await using var vm = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([primary, second]);
        vm.SelectedImage = second;
        var window = new MainWindow
        {
            Width = 900,
            Height = 700,
            DataContext = vm
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        DeleteConfirmationRequest? prompt = null;
        var prompted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ConfirmDeleteAsync = request =>
        {
            prompt = request;
            prompted.TrySetResult();
            return Task.FromResult(false);
        };

        var browse = window.FindControl<BrowseGridView>("BrowseGridView")!;
        var tile = Assert.Single(browse.GetVisualDescendants().OfType<Border>(),
            border => ReferenceEquals(border.DataContext, second) &&
                      border.Classes.Contains("thumbnail"));
        var menu = tile.ContextMenu!;
        menu.PlacementTarget = tile;
        menu.Open();
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<MenuItem>(menu.Items.ElementAt(5))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await prompted.Task.WaitAsync(TestWaits.Condition);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(second, Assert.Single(prompt!.Versions));
        Assert.Contains(
            "The original file is not affected.",
            MainWindow.DeleteConfirmationContent(prompt).Message);
        Assert.Contains(second, vm.Browse.AllImages);
        var states = await catalog.LoadImageStatesAsync([path]);
        Assert.Equal(
            new[] { 1, 2 },
            states[path].Select(state => state.Version).ToArray());
        window.DataContext = null;
        window.Close();
    }

    [AvaloniaFact]
    public void ThumbnailRightClick_SelectsOutsideTargetAndPreservesInsideSelection()
    {
        var images = new[]
        {
            new ImageFile(Path.Combine(Path.GetTempPath(), "first.jpg")),
            new ImageFile(Path.Combine(Path.GetTempPath(), "second.jpg")),
            new ImageFile(Path.Combine(Path.GetTempPath(), "third.jpg"))
        };
        var control = new BrowseGridView
        {
            Images = new ObservableCollection<ImageFile>(images)
        };
        images[0].IsSelected = true;
        images[1].IsSelected = true;

        control.ApplyRightClickSelection(images[1]);

        Assert.True(images[0].IsSelected);
        Assert.True(images[1].IsSelected);
        Assert.False(images[2].IsSelected);
        Assert.Same(images[1], control.SelectedImage);

        control.ApplyRightClickSelection(images[2]);

        Assert.False(images[0].IsSelected);
        Assert.False(images[1].IsSelected);
        Assert.True(images[2].IsSelected);
        Assert.Same(images[2], control.SelectedImage);
    }

    [AvaloniaFact]
    public void BrowseActions_MenuOwnsSelectionActionsAndTracksSelectionState()
    {
        using var catalog = new CatalogService(NewRoot());
        var vm = NewViewModel(catalog);
        var image = new ImageFile(Path.Combine(Path.GetTempPath(), "selection-menu.jpg"));
        vm.Browse.SetImages([image]);
        var control = new BrowseGridView { DataContext = vm };
        var window = new Window { Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var panel = control.FindControl<StackPanel>("BrowseActionsPanel")!;
        var thumbnailControls = control.FindControl<StackPanel>(
            "ThumbnailSizePanel")!;
        var loupeView = Assert.IsType<ToggleButton>(
            control.FindControl<ToggleButton>("LoupeViewButton"));
        var compareView = Assert.IsType<ToggleButton>(
            control.FindControl<ToggleButton>("CompareViewButton"));
        var actions = control.FindControl<Button>("BrowseActionsButton")!;
        var flyout = Assert.IsType<MenuFlyout>(actions.Flyout);
        flyout.ShowAt(actions);
        Dispatcher.UIThread.RunJobs();
        var items = flyout.Items.ToArray();
        var selectAll = Assert.IsType<MenuItem>(items[0]);
        var deselectAll = Assert.IsType<MenuItem>(items[1]);
        Assert.IsType<Separator>(items[2]);
        var deleteRejected = Assert.IsType<MenuItem>(items[3]);

        Assert.Equal([actions], panel.Children);
        Assert.Same(loupeView, thumbnailControls.Children[0]);
        Assert.Same(compareView, thumbnailControls.Children[1]);
        Assert.IsType<Rectangle>(thumbnailControls.Children[2]);
        Assert.Null(control.FindControl<Button>("GridViewButton"));
        Assert.DoesNotContain("accent", actions.Classes);
        Assert.Null(control.FindControl<Button>("SelectAllButton"));
        Assert.Null(control.FindControl<Button>("SelectNoneButton"));
        Assert.DoesNotContain(
            panel.Children.OfType<TextBlock>(),
            text => text.Text == "Select");
        Assert.Equal(
            ["Select All", "Deselect All", "Delete Rejected…"],
            new[] { selectAll, deselectAll, deleteRejected }
                .Select(item => item.Header));
        Assert.Equal("Ctrl+A", selectAll.InputGesture?.ToString());
        Assert.Equal("Ctrl+D", deselectAll.InputGesture?.ToString());
        Assert.All(
            new[] { selectAll, deselectAll, deleteRejected },
            item => Assert.True(item.IsVisible));

        Assert.Null(vm.SelectedImage);
        Assert.False(vm.HasSelectedImage);
        Assert.True(actions.IsEnabled);
        Assert.True(selectAll.IsEnabled);
        Assert.False(deselectAll.IsEnabled);
        Assert.True(deleteRejected.IsEnabled);

        vm.Browse.ToggleSelection(image);
        Dispatcher.UIThread.RunJobs();
        Assert.True(deselectAll.IsEnabled);

        var selectRequests = 0;
        var deselectRequests = 0;
        var deleteRequests = 0;
        control.SelectAllRequested += (_, _) => selectRequests++;
        control.DeselectAllRequested += (_, _) => deselectRequests++;
        control.DeleteRejectedRequested += (_, _) => deleteRequests++;
        selectAll.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        deselectAll.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        deleteRejected.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal((1, 1, 1), (selectRequests, deselectRequests, deleteRequests));

        vm.Browse.SetImages([]);
        Dispatcher.UIThread.RunJobs();
        Assert.False(actions.IsEnabled);
        Assert.False(selectAll.IsEnabled);
        Assert.False(deselectAll.IsEnabled);
        Assert.False(deleteRejected.IsEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task SelectionKeyBindings_AreSymmetricInDevelopAndFullScreen()
    {
        var root = NewRoot();
        using var catalog = new CatalogService(root);
        await using var vm = NewViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = new[]
        {
            new ImageFile(Path.Combine(root, "first.jpg")),
            new ImageFile(Path.Combine(root, "second.jpg"))
        };
        vm.Browse.SetImages(images);
        vm.IsDevelopMode = true;
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var selectAll = Assert.Single(
            window.KeyBindings,
            binding => binding.Gesture.ToString() == "Ctrl+A");
        var deselectAll = Assert.Single(
            window.KeyBindings,
            binding => binding.Gesture.ToString() == "Ctrl+D");
        Assert.Same(vm.SelectAllCommand, selectAll.Command);
        Assert.Same(vm.DeselectAllCommand, deselectAll.Command);

        selectAll.Command!.Execute(null);
        Assert.All(images, image => Assert.True(image.IsSelected));
        deselectAll.Command!.Execute(null);
        Assert.All(images, image => Assert.False(image.IsSelected));

        vm.IsFullScreenMode = true;
        selectAll.Command.Execute(null);
        Assert.All(images, image => Assert.False(image.IsSelected));
        vm.Browse.SelectAllVisible();
        deselectAll.Command.Execute(null);
        Assert.All(images, image => Assert.True(image.IsSelected));
        window.Close();
    }

    [Fact]
    public void ShortcutCatalog_ListsDeselectAllBesideSelectAll()
    {
        var organize = Assert.Single(
            ShortcutCatalog.Groups,
            group => group.Title == "Organize");
        var selectAllIndex = organize.Entries.ToList().FindIndex(
            entry => entry.Keys == "Ctrl+A");

        Assert.True(selectAllIndex >= 0);
        Assert.Equal("Ctrl+D", organize.Entries[selectAllIndex + 1].Keys);
        Assert.Equal(
            "Deselect all visible images",
            organize.Entries[selectAllIndex + 1].Action);
    }

    private static string NewRoot() =>
        Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"happy-photon-selection-menu-{Guid.NewGuid():N}")).FullName;

    private static MainWindowViewModel NewViewModel(CatalogService catalog) =>
        new(catalog, baseLoader: null, loadMetadataAsync: _ => Task.CompletedTask);
}

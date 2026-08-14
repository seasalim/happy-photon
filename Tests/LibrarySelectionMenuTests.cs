using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LibrarySelectionMenuTests
{
    [AvaloniaFact]
    public void LibraryActions_MenuOwnsSelectionActionsAndTracksSelectionState()
    {
        using var catalog = new CatalogService(NewRoot());
        var vm = NewViewModel(catalog);
        var image = new ImageFile(Path.Combine(Path.GetTempPath(), "selection-menu.jpg"));
        vm.Library.SetImages([image]);
        var control = new LibraryGridView { DataContext = vm };
        var window = new Window { Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var panel = control.FindControl<StackPanel>("LibraryActionsPanel")!;
        var export = control.FindControl<Button>("LibraryExportButton")!;
        var actions = control.FindControl<Button>("LibraryActionsButton")!;
        var flyout = Assert.IsType<MenuFlyout>(actions.Flyout);
        flyout.ShowAt(actions);
        Dispatcher.UIThread.RunJobs();
        var items = flyout.Items.ToArray();
        var selectAll = Assert.IsType<MenuItem>(items[0]);
        var deselectAll = Assert.IsType<MenuItem>(items[1]);
        Assert.IsType<Separator>(items[2]);
        var deleteRejected = Assert.IsType<MenuItem>(items[3]);

        Assert.Equal([export, actions], panel.Children);
        Assert.Contains("accent", export.Classes);
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

        vm.Library.ToggleSelection(image);
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

        vm.Library.SetImages([]);
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
        vm.Library.SetImages(images);
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
        vm.Library.SelectAllVisible();
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

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
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
        using var catalog = new CatalogService(temporary.Path);
        await using var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        viewModel.SetRootFolder(temporary.Path, selectRoot: false);
        var image = new ImageFile(Path.Combine(temporary.Path, "image.jpg"));
        viewModel.Library.SetImages([image]);
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
            var selectedTreeBounds = folderTree.Bounds;
            var selectedNavigatorBounds = navigator.Bounds;

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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BrowseGridDensityGateTests
{
    [AvaloniaFact]
    public async Task ShiftNavigationKeys_ExtendFromAnchorAndPlainMovesReanchor()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var images = NewImages(12).ToArray();
        viewModel.Browse.SetImages(images);
        viewModel.SelectedImage = images[0];
        var window = new MainWindow
        {
            Width = 1400,
            Height = 800
        };
        using var windowScope = TestUiScope.ForMainWindow(window, viewModel);
        Dispatcher.UIThread.RunJobs();

        var grid = window.FindControl<BrowseGridView>("BrowseGridView")!;
        grid.Width = 777;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(4, grid.GetItemsPerRow());
        Assert.True(grid.Focus());

        Press(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Assert.Same(images[1], viewModel.SelectedImage);
        Assert.Equal(images[0..2], viewModel.Browse.GetSelectedImages());

        Press(window, Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Assert.Same(images[5], viewModel.SelectedImage);
        Assert.Equal(images[0..6], viewModel.Browse.GetSelectedImages());

        Press(window, Key.Left, PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        Assert.Equal(images[0..5], viewModel.Browse.GetSelectedImages());
        Assert.Equal(5, viewModel.SelectedCount);

        Press(window, Key.Right, PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Same(images[5], Assert.Single(viewModel.Browse.GetSelectedImages()));
        Assert.Same(images[5], grid.SelectionAnchor);

        var tile = window.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Name == "ThumbnailTile" &&
                              ReferenceEquals(border.DataContext, images[7]));
        var point = tile.TranslatePoint(
            new Point(tile.Bounds.Width / 2, tile.Bounds.Height / 2),
            window)!.Value;
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(images[5..8], viewModel.Browse.GetSelectedImages());
    }

    private static void Press(
        Window window,
        Key key,
        PhysicalKey physicalKey,
        RawInputModifiers modifiers)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
        Dispatcher.UIThread.RunJobs();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using Xunit.Sdk;

namespace HappyPhoton.Tests;

public sealed class ShowcaseTests
{
    private static readonly PixelSize DevelopSize = new(1200, 700);

    [AvaloniaTheory]
    [InlineData("develop-basic", false, false)]
    [InlineData("develop-assessment-dark", false, true)]
    [InlineData("develop-assessment-mid-gray", true, true)]
    public Task Develop_RendersShowcase(string scene, bool midGray, bool assessment) =>
        CaptureDevelopSceneAsync(
            scene,
            midGray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark,
            assessment);

    [AvaloniaTheory]
    [InlineData("folder-tree-rest", false)]
    [InlineData("folder-tree-hover", true)]
    public async Task FolderTree_RendersShowcase(string scene, bool hover)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        var browseRoot = Directory.CreateDirectory(Path.Combine(
            root.Path,
            "photos")).FullName;
        for (var index = 1; index <= 40; index++)
        {
            Directory.CreateDirectory(Path.Combine(
                browseRoot,
                $"Folder {index:00}"));
        }

        viewModel.SetRootFolder(browseRoot, selectRoot: false);
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = viewModel };

        try
        {
            ShowcaseTestHelper.Capture(
                scene,
                window,
                DevelopSize,
                ThemeVariant.Dark,
                hover ? (Action<Window>)(stagedWindow =>
                {
                    var tree = stagedWindow.FindControl<FolderTreePanel>(
                        "FolderTreePanel")!.FindControl<TreeView>("FolderTree")!;
                    var point = tree.TranslatePoint(
                        new Point(tree.Bounds.Width / 2, tree.Bounds.Height / 2),
                        stagedWindow)!.Value;
                    stagedWindow.MouseMove(point, RawInputModifiers.None);
                }) : null);
        }
        finally
        {
            window.DataContext = null;
        }
    }

    [AvaloniaFact]
    public void SizeMismatch_ThrowsAssertionAndClosesWindow()
    {
        var expectedSize = new PixelSize(200, 120);
        var actualSize = new PixelSize(201, 120);
        ShowcaseTestHelper.Capture(
            "deliberate-size-match",
            new Window { Content = new Border() },
            expectedSize,
            ThemeVariant.Dark);

        var window = new Window { Content = new Border() };

        var exception = Assert.Throws<EqualException>(() => ShowcaseTestHelper.Capture(
            "deliberate-size-mismatch", window, expectedSize,
            ThemeVariant.Dark,
            stagedWindow => stagedWindow.Width = actualSize.Width));

        Assert.Contains(expectedSize.ToString(), exception.Message);
        Assert.Contains(actualSize.ToString(), exception.Message);
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public void TraversalScene_IsRejectedWithoutWriting()
    {
        var uniqueName = $"showcase-traversal-{Guid.NewGuid():N}";
        var scene = $"../{uniqueName}";
        var escapedPath = Path.GetFullPath(Path.Combine(
            GoldenTestPaths.RepositoryRoot, "artifacts", "shots", $"{scene}.png"));

        Assert.Throws<ArgumentException>(() => ShowcaseTestHelper.Capture(
            scene, new Window(), new PixelSize(200, 120), ThemeVariant.Dark));
        Assert.False(File.Exists(escapedPath));
    }

    private static async Task CaptureDevelopSceneAsync(
        string scene, ThemeVariant theme, bool assessment)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var viewModel = new MainWindowViewModel(
            catalog,
            baseLoader: null,
            loadMetadataAsync: _ => Task.CompletedTask);
        var assetPath = GoldenTestPaths.Asset("srgb-reference.jpg");
        using var bitmap = new Bitmap(assetPath);
        var image = new ImageFile(assetPath);
        viewModel.Browse.SetImages([image]);
        viewModel.SelectedImage = image;
        viewModel.IsDevelopMode = true;
        viewModel.PreviewImage = bitmap;
        viewModel.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = viewModel };

        try
        {
            ShowcaseTestHelper.Capture(
                scene, window, DevelopSize, theme,
                stagedWindow =>
                {
                    viewModel.PreviewImage = bitmap;
                    stagedWindow.FindControl<DevelopEditPanel>(
                        "DevelopEditPanel")!.FindControl<ScrollViewer>(
                            "DevelopControlsScrollViewer")!.Offset = default;
                    if (assessment)
                    {
                        viewModel.ToggleColorAssessmentModeCommand.Execute(null);
                    }
                });
        }
        finally
        {
            viewModel.PreviewImage = null;
            window.DataContext = null;
        }
    }
}

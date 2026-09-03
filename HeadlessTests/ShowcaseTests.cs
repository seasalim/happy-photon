using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
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

    [AvaloniaFact]
    public async Task BrowseGridRest_RendersShowcase()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        viewModel.Browse.SetImages(Enumerable.Range(0, 60)
            .Select(index => new ImageFile($"browse-grid-{index:D2}.jpg"))
            .ToArray());
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var window = new MainWindow { DataContext = viewModel };

        try
        {
            ShowcaseTestHelper.Capture(
                "browse-grid-rest",
                window,
                DevelopSize,
                ThemeVariant.Dark);
        }
        finally
        {
            window.DataContext = null;
        }
    }

    [AvaloniaTheory]
    [InlineData("develop-history-rest", false)]
    [InlineData("develop-history-hover", true)]
    public async Task DevelopHistory_RendersShowcase(string scene, bool hover)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await catalog.InitializeAsync();
        await using var viewModel = new MainWindowViewModel(
            catalog,
            new NullBaseLoader(),
            _ => Task.CompletedTask);
        var assetPath = GoldenTestPaths.Asset("srgb-reference.jpg");
        using var bitmap = new Bitmap(assetPath);
        var settings = new EditSettings { Exposure = .39 };
        var image = new ImageFile(assetPath) { EditSettings = settings };
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        var entries = Enumerable.Range(0, 40)
            .Select(index => new CatalogEditHistoryEntry(
                index,
                index == 0 ? "Original" : $"Exposure +{index / 100d:0.00}",
                new EditSettings { Exposure = index / 100d }))
            .ToArray();
        await catalog.SaveEditSettingsWithHistoryAsync(
            image.CatalogId,
            settings,
            new CatalogEditHistoryMutation(-1, entries, 39));
        viewModel.ShowWorkspaceReady(
            MainWindowViewModel.CurrentFirstRunExperienceVersion);
        viewModel.Browse.SetImages([image]);
        viewModel.IsDevelopMode = true;
        viewModel.SelectedImage = image;
        await TestWaits.UntilAsync(() => viewModel.IsHistoryLoaded);
        viewModel.PreviewImage = bitmap;
        var window = new MainWindow { DataContext = viewModel };

        try
        {
            ShowcaseTestHelper.Capture(
                scene,
                window,
                DevelopSize,
                ThemeVariant.Dark,
                stagedWindow =>
                {
                    viewModel.PreviewImage = bitmap;
                    if (hover)
                    {
                        var scroll = stagedWindow.GetVisualDescendants()
                            .OfType<ScrollViewer>()
                            .Single(candidate => candidate.Name ==
                                "HistoryScrollViewer");
                        var point = scroll.TranslatePoint(
                            new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2),
                            stagedWindow)!.Value;
                        stagedWindow.MouseMove(point, RawInputModifiers.None);
                        SettleRevealedThumb(scroll);
                    }
                });
        }
        finally
        {
            viewModel.PreviewImage = null;
            window.DataContext = null;
        }
    }

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
                    SettleRevealedThumb(tree);
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

    private static void SettleRevealedThumb(Visual host)
    {
        var thumb = host.GetVisualDescendants().OfType<ScrollBar>()
            .Single(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical)
            .GetVisualDescendants().OfType<Thumb>().Single();
        ShowcaseTestHelper.Settle(() => thumb.Opacity >= 1, "The revealed thumb");
    }
}

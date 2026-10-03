using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ReadmeScreenshotTests
{
    private static readonly PixelSize CaptureSize = new(2204, 1263);

    private const double RenderScaling = 1.5;

    // Committed CC0 assets from Tests/assets, plus CC0 compatibility fixtures that
    // capture-readme-screenshots.ps1 fetches and verifies into artifacts/compatibility-fixtures.
    private static readonly DemoPhoto[] Photos =
    [
        new("canon-eos-350d.cr2", "canal-at-dusk.CR2", 4, ColorLabel.Yellow, ImageFlag.Picked),
        new("canon-eos-6d-iso-6400.cr2", "milky-way.CR2", 5, ColorLabel.Blue, ImageFlag.Picked),
        new("pentax-k-r.dng", "paddock.DNG", 0, ColorLabel.None, ImageFlag.Rejected),
        new("panasonic-s9-standard.RW2", "street.RW2", 3, ColorLabel.None, ImageFlag.Unflagged),
        new("canon-r5m2-raw-apsc.CR3", "tomatoes.CR3", 2, ColorLabel.Red, ImageFlag.Unflagged),
        new("sony-a9m3-lossy.ARW", "tool-wall.ARW", 3, ColorLabel.None, ImageFlag.Unflagged),
        new("fuji-xt50-compressed.RAF", "trees.RAF", 4, ColorLabel.Purple, ImageFlag.Unflagged),
        new("fujifilm-x30.raf", "valley.RAF", 5, ColorLabel.Green, ImageFlag.Picked)
    ];

    [AvaloniaTheory]
    [InlineData("readme-browse", false, false)]
    [InlineData("readme-develop", true, false)]
    [InlineData("readme-develop-midgray-assess", true, true)]
    public async Task CaptureReadme(string scene, bool develop, bool assess)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("HAPPY_PHOTON_README_SHOTS") == "1",
            "Opt in with scripts/capture-readme-screenshots.ps1.");

        using var fixture = new CatalogVmFixture("readme-shots");
        var demo = Directory.CreateDirectory(fixture.Path("Demo")).FullName;
        CopyPhotos(demo);
        using var catalog = await fixture.CreateCatalogAsync("catalog");

        if (develop)
        {
            await SeedEditsAsync(catalog, Path.Combine(demo, assess ? "canal-at-dusk.CR2" : "valley.RAF"), assess);
        }

        await using var vm = fixture.CreateViewModel(catalog);
        await vm.InitializeAsync();
        vm.RestoreAppTheme(assess ? AppTheme.MidGray : AppTheme.Dark);
        vm.RestoreTipsSettings(new AppSettings
        {
            BrowseTipsSeen = true,
            DevelopTipsSeen = true,
            ExportTipsSeen = true
        });
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.RestoreBrowseThumbnailSize(BrowseThumbnailSize.Large);
        vm.SetRootFolder(demo);
        await TestWaits.UntilAsync(() => vm.Browse.TotalCount == Photos.Length);
        vm.RequestThumbnailRange(0, Photos.Length);
        await TestWaits.UntilAsync(() => vm.Browse.AllImages.All(image =>
            image.ThumbnailSatisfies(vm.BrowseThumbnailRequest)));
        StageAssessments(vm);
        vm.SelectedImage = vm.Browse.AllImages.Single(image =>
            image.FileName == (assess ? "canal-at-dusk.CR2" : "valley.RAF"));
        vm.SelectedImage.IsSelected = true;
        vm.RefreshSelectedCount();

        if (develop)
        {
            vm.SwitchToDevelopCommand.Execute(null);
            await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded &&
                !vm.IsDevelopPreviewLoading);
            vm.IsColorAssessmentMode = assess;
            Assert.NotEmpty(vm.HistoryEntries);
        }

        await TestWaits.UntilAsync(() => vm.SelectedImage.MetadataLoaded && vm.Histogram != null &&
            !vm.IsBackgroundActivityStatusVisible);
        var theme = assess ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, CaptureSize, theme, renderedWindow =>
        {
            renderedWindow.SetRenderScaling(RenderScaling);
            renderedWindow.Width = CaptureSize.Width / RenderScaling;
            renderedWindow.Height = CaptureSize.Height / RenderScaling;

            if (!develop)
            {
                var workspace = renderedWindow.FindControl<Border>("WorkspaceLeftPanel")!
                    .GetVisualParent<Grid>()!;
                workspace.ColumnDefinitions[4].Width = new GridLength(280);
            }

            renderedWindow.UpdateLayout();

            if (develop)
            {
                ShowcaseTestHelper.SettleExpanderChevrons(renderedWindow);
            }

            Assert.Equal(RenderScaling, renderedWindow.RenderScaling);
            Assert.False(vm.IsBrowseTipsVisible || vm.IsDevelopTipsVisible);
            Assert.Contains(renderedWindow.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == $"{Photos.Length} photos · 1 selected");
            Assert.All(vm.Browse.AllImages, image => Assert.NotNull(image.Thumbnail));
        });
    }

    private static void CopyPhotos(string demo)
    {
        var availability = new SourceAvailabilityService();

        foreach (var photo in Photos)
        {
            var committed = GoldenTestPaths.Asset(photo.Source);
            var path = File.Exists(committed)
                ? committed
                : Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "compatibility-fixtures", photo.Source);
            Assert.Equal(SourceAvailability.AvailableLocally, availability.GetAvailability(path));
            File.Copy(path, Path.Combine(demo, photo.Name));
        }
    }

    private static void StageAssessments(MainWindowViewModel vm)
    {
        foreach (var photo in Photos)
        {
            var image = vm.Browse.AllImages.Single(image => image.FileName == photo.Name);
            image.IsSelected = false;
            image.Rating = photo.Rating;
            image.ColorLabel = photo.Label;
            image.Flag = photo.Flag;
        }
    }

    private static async Task SeedEditsAsync(CatalogService catalog, string path, bool assess)
    {
        var id = await catalog.GetOrCreateImageAsync(path);
        var settings = new EditSettings();
        var edits = assess
            ? new (string Label, Action<EditSettings> Apply)[]
            {
                ("Exposure", edit => edit.Exposure = 0.70),
                ("Contrast", edit => edit.Contrast = 20),
                ("Shadows", edit => edit.Shadows = 41),
                ("Vibrance", edit => edit.Vibrance = 41)
            }
            : new (string Label, Action<EditSettings> Apply)[]
            {
                ("Exposure", edit => edit.Exposure = -0.15),
                ("Contrast", edit => edit.Contrast = 45),
                ("Highlights", edit => edit.Highlights = -35),
                ("Shadows", edit => edit.Shadows = 25),
                ("Vibrance", edit => edit.Vibrance = 60),
                ("Saturation", edit => edit.Saturation = 22),
                ("Curve", edit => edit.Curve = new CurveData
                {
                    Points = [new(0, 0), new(0.25, 0.20), new(0.5, 0.5), new(0.75, 0.80), new(1, 1)]
                })
            };

        foreach (var (label, apply) in edits)
        {
            var before = settings.Clone();
            apply(settings);
            await catalog.SaveEditSettingsWithHistoryAsync(id, settings, null, before: before, historyLabel: label);
        }
    }

    private sealed record DemoPhoto(
        string Source, string Name, int Rating, ColorLabel Label, ImageFlag Flag);
}

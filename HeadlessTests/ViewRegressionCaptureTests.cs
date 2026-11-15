using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

/// <summary>
/// Deterministic full-window captures used to prove a view refactor changes no pixels.
/// Run through scripts/capture-view-regression.ps1, which fails when a scene is skipped.
/// </summary>
public sealed class ViewRegressionCaptureTests
{
    private const string OptIn = "HAPPY_PHOTON_VIEW_CAPTURES";

    // The edited photo is a JPEG: its decode and render are deterministic across processes.
    private const string Subject = "reference.jpg";

    private static readonly DemoPhoto[] Photos =
    [
        new("canon-eos-350d.cr2", "canal-at-dusk.CR2", 4, ColorLabel.Yellow, ImageFlag.Picked),
        new("canon-eos-6d-iso-6400.cr2", "milky-way.CR2", 2, ColorLabel.Blue, ImageFlag.Rejected),
        new("fujifilm-x30.raf", "valley.RAF", 5, ColorLabel.Green, ImageFlag.Unflagged),
        new("srgb-reference.jpg", "reference.jpg", 3, ColorLabel.Purple, ImageFlag.Picked)
    ];

    [AvaloniaFact]
    public async Task Browse()
    {
        await CaptureAsync("view-browse", new PixelSize(1600, 1000), WorkspaceMode.Browse, null);
    }

    [AvaloniaFact]
    public async Task Develop()
    {
        await CaptureAsync("view-develop", new PixelSize(1400, 3000), WorkspaceMode.Develop,
            vm =>
            {
                vm.ActiveMixerBand = ColorMixerBand.Blue;

                return Task.CompletedTask;
            });
    }

    [AvaloniaFact]
    public async Task DevelopLocals()
    {
        await CaptureAsync("view-develop-locals", new PixelSize(1400, 2000), WorkspaceMode.Develop,
            async vm =>
            {
                await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
                await TestWaits.UntilAsync(() => vm.IsLocalsMode && vm.Locals.Count > 0);
                vm.SelectedLocal = vm.Locals[0];
            });
    }

    [AvaloniaFact]
    public async Task DevelopCrop()
    {
        await CaptureAsync("view-develop-crop", new PixelSize(1600, 1000), WorkspaceMode.Develop,
            async vm =>
            {
                await vm.ToggleCropModeCommand.ExecuteAsync(null);
                await TestWaits.UntilAsync(() => vm.CanChooseCropRatio);
                // A square crop on a landscape frame shows the side masks.
                vm.ChooseCropRatio("1:1");
            });
    }

    [AvaloniaFact]
    public async Task Export()
    {
        // The watermark stays off so its options render with the disabled dimming.
        await CaptureAsync("view-export", new PixelSize(1600, 1600), WorkspaceMode.Export, null);
    }

    private static async Task CaptureAsync(
        string scene, PixelSize size, WorkspaceMode mode, Func<MainWindowViewModel, Task>? stage)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(OptIn) == "1",
            "Opt in with scripts/capture-view-regression.ps1.");

        using var fixture = new CatalogVmFixture("view-captures");
        var demo = Directory.CreateDirectory(fixture.Path("Demo")).FullName;
        CopyPhotos(demo);
        using var catalog = await fixture.CreateCatalogAsync("catalog");
        await SeedEditsAsync(catalog, Path.Combine(demo, Subject));

        await using var vm = fixture.CreateViewModel(catalog);
        await vm.InitializeAsync();
        vm.RestoreAppTheme(AppTheme.Dark);
        vm.RestoreTipsSettings(new AppSettings
        {
            BrowseTipsSeen = true,
            DevelopTipsSeen = true,
            ExportTipsSeen = true
        });
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.SetRootFolder(demo);
        await TestWaits.UntilAsync(() => vm.Browse.TotalCount == Photos.Length);
        vm.RequestThumbnailRange(0, Photos.Length);
        await TestWaits.UntilAsync(() => vm.Browse.AllImages.All(image =>
            image.ThumbnailSatisfies(vm.BrowseThumbnailRequest)));
        StageAssessments(vm);
        vm.SelectedImage = vm.Browse.AllImages.Single(image => image.FileName == Subject);
        vm.SelectedImage.IsSelected = true;
        vm.RefreshSelectedCount();

        if (mode == WorkspaceMode.Develop)
        {
            vm.SwitchToDevelopCommand.Execute(null);
            await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded &&
                !vm.IsDevelopPreviewLoading);
        }
        else if (mode == WorkspaceMode.Export)
        {
            await vm.SwitchToExportCommand.ExecuteAsync(null);
        }

        if (stage is not null) await stage(vm);

        await TestWaits.UntilAsync(() => vm.SelectedImage.MetadataLoaded &&
            !vm.IsBackgroundActivityStatusVisible &&
            (mode != WorkspaceMode.Develop || (vm.Histogram != null && !vm.IsDevelopPreviewLoading)));

        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, size, ThemeVariant.Dark, shown =>
        {
            shown.UpdateLayout();
            if (mode == WorkspaceMode.Develop) SettlePreview(vm);

            if (mode != WorkspaceMode.Browse) ShowcaseTestHelper.SettleExpanderChevrons(shown);

            Assert.Equal(mode, vm.WorkspaceMode);
            Assert.All(vm.Browse.AllImages, image => Assert.NotNull(image.Thumbnail));
            Assert.NotEmpty(shown.GetVisualDescendants().OfType<TextBlock>());
        });
    }

    // The preview re-renders for the capture-size viewport; capture once it has stopped changing.
    private static void SettlePreview(MainWindowViewModel vm)
    {
        var preview = vm.PreviewImage;
        var histogram = vm.Histogram;
        var stableSince = DateTime.UtcNow;

        ShowcaseTestHelper.Settle(() =>
        {
            if (!ReferenceEquals(preview, vm.PreviewImage) || !ReferenceEquals(histogram, vm.Histogram))
            {
                preview = vm.PreviewImage;
                histogram = vm.Histogram;
                stableSince = DateTime.UtcNow;
            }

            return !vm.IsDevelopPreviewLoading && DateTime.UtcNow - stableSince > TimeSpan.FromSeconds(3);
        }, "Develop preview");
    }

    private static void CopyPhotos(string demo)
    {
        foreach (var photo in Photos)
        {
            File.Copy(GoldenTestPaths.Asset(photo.Source), Path.Combine(demo, photo.Name));
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

    private static async Task SeedEditsAsync(CatalogService catalog, string path)
    {
        var id = await catalog.GetOrCreateImageAsync(path);
        var settings = new EditSettings
        {
            Exposure = -0.15,
            Contrast = 45,
            Vibrance = 30,
            Locals = [new() { Id = "00000000000000000000000000000001", Type = "radial", Ordinal = 1 }]
        };
        await catalog.SaveEditSettingsAsync(id, settings);
    }

    private sealed record DemoPhoto(
        string Source, string Name, int Rating, ColorLabel Label, ImageFlag Flag);
}

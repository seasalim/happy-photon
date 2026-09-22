using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportFeedbackShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("export-proof-web-dark", 1200, 700)]
    [InlineData("export-proof-min-gray", 800, 500)]
    [InlineData("export-exporting-gray", 800, 500)]
    [InlineData("export-stopped-dark", 1200, 700)]
    public async Task RenderShowcase(string scene, int width, int height)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog, new StandardBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var photos = Enumerable.Range(1, 6).Select(index =>
        {
            var path = Path.Combine(root.Path, $"photo-{index}.jpg");
            File.Copy(GoldenTestPaths.Asset("srgb-reference.jpg"), path);
            return new ImageFile(path);
        }).ToArray();
        vm.Browse.SetImages(photos);
        vm.Browse.SelectAllVisible();
        vm.RefreshSelectedCount();
        foreach (var photo in photos)
            vm.Browse.ReplaceThumbnail(photo, new Bitmap(photo.FilePath));
        vm.ExportSettings.OutputFolder = Path.Combine(root.Path, "finished");
        vm.ExportSettings.ExportWeb = true;
        vm.SwitchToExportCommand.Execute(null);
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        if (scene.StartsWith("export-proof", StringComparison.Ordinal))
        {
            vm.ExportSettings.ExportSmall = true;
            vm.SelectedExportProofSize = vm.ExportProofSizes.Single(size => size.Name == "Web");
            vm.ExportSettings.ShowProof = true;
            await TestWaits.UntilAsync(() => vm.ExportProofCaption == "PROOF · Web · 2048 PX · sRGB");
        }
        else if (scene == "export-exporting-gray")
        {
            vm.IsExportJobRunning = true;
            vm.ExportProgressValue = 4;
            vm.ExportProgressMaximum = 12;
            vm.ExportProgressText = "Exporting 4 of 12 files";
        }
        else
        {
            var job = vm.ExportSettings.CreateJob(photos);
            vm.ExportReport = ExportRunReport.FromResult(new ExportBatchResult(job,
                job.Targets.Take(2).Select(target => new ExportTargetOutcome(
                    target.Capture, target.Recipe, target.ResolvedPath, null)).ToArray(),
                stopped: true));
        }
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(width, height),
            width == 800 ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
    }
}

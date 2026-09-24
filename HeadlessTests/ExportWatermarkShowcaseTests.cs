using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ExportWatermarkShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("export-watermark-default")]
    [InlineData("export-watermark-vertical")]
    [InlineData("export-watermark-off")]
    public async Task RenderShowcase(string scene)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        await using var vm = new MainWindowViewModel(catalog, new StandardBaseLoader(),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        var photos = Enumerable.Range(1, 3).Select(index =>
        {
            var path = Path.Combine(root.Path, $"photo-{index}.jpg");
            File.Copy(GoldenTestPaths.Asset("srgb-reference.jpg"), path);
            return new ImageFile(path);
        }).ToArray();
        vm.Browse.SetImages(photos);
        vm.Browse.SelectAllVisible();
        vm.RefreshSelectedCount();
        foreach (var photo in photos) vm.Browse.ReplaceThumbnail(photo, new Bitmap(photo.FilePath));
        vm.ExportSettings.OutputFolder = Path.Combine(root.Path, "finished");
        vm.ExportSettings.ExportWeb = true;
        vm.SwitchToExportCommand.Execute(null);
        await TestWaits.UntilAsync(() => vm.PreviewImage != null);
        var mark = vm.ExportSettings.Watermark;
        mark.Text = "© Jane Doe 2026";
        mark.Enabled = scene != "export-watermark-off";
        if (scene == "export-watermark-vertical")
        {
            mark.Edge = WatermarkEdge.Right;
            mark.Size = 2.5;
        }
        vm.ExportSettings.ShowProof = true;
        await TestWaits.UntilAsync(() => vm.ExportProofCaption == "PROOF · Full size · No resizing · sRGB");
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 760), ThemeVariant.Dark, shown =>
        {
            vm.IsWatermarkExpanded = true;
            var pane = shown.GetVisualDescendants().OfType<ExportSettingsPane>().Single();
            var expander = pane.FindControl<Expander>("ExportWatermarkExpander")!;
            ShowcaseTestHelper.Settle(() => expander.GetVisualDescendants()
                .OfType<ExportWatermarkOptions>().Any(control => control.Bounds.Height > 0), "Watermark expander");
            var scroll = pane.FindControl<ScrollViewer>("ExportSettingsScroll")!;
            scroll.Offset = new Vector(0, expander.TranslatePoint(default, (Visual)scroll.Content!)!.Value.Y);
            Dispatcher.UIThread.RunJobs();
            ShowcaseTestHelper.SettleExpanderChevrons(pane);
            Assert.Equal(scene switch
                {
                    "export-watermark-off" => "Off",
                    "export-watermark-vertical" => "\"© Jane Doe 2026\" · Right edge · Bottom · Rotated",
                    _ => "\"© Jane Doe 2026\" · Bottom right"
                },
                vm.WatermarkSummary);
        });
    }
}

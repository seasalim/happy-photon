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

public sealed class SpotVisualizationShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("spots-visualize-fit")]
    [InlineData("spots-visualize-1to1")]
    public async Task RenderScene(string scene)
    {
        using var fixture = new CatalogVmFixture("visualize-shots");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("canon-eos-6d-iso-6400.cr2");
        GoldenTestPaths.RequireReadableFixture(path);
        await using var vm = fixture.CreateViewModel(catalog, new RawBaseLoader(), _ => Task.CompletedTask);
        var image = new ImageFile(path)
        {
            EditSettings = new EditSettings { Repairs = HealWorkloads.Repairs(HealWorkloads.S64()) }
        };
        image.CatalogId = await catalog.GetOrCreateImageAsync(path);
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        vm.SelectedSpot = vm.Spots[0];
        vm.VisualizeSpots = true;
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700), ThemeVariant.Dark, shown =>
        {
            var viewer = shown.GetVisualDescendants().OfType<ZoomPanControl>().Single(v => v.Name == "ZoomPanControl");

            if (scene.EndsWith("1to1"))
            {
                vm.IsZoomFitMode = false;
                vm.ZoomLevel = 1;
                var edge = Math.Max(vm.OriginalViewPixelSize.Width, vm.OriginalViewPixelSize.Height);
                vm.PublishRequiredDeviceLongEdge(edge);
                ShowcaseTestHelper.Settle(() => vm.PreviewImage?.PixelSize.Width == edge && vm.OneToOneStatus == "1:1", "Full-resolution source bitmap");
                viewer.ApplyNormalizedViewport(new(new(.4, .4), 1 / viewer.GetFitZoomLevel()));
            }

            ShowcaseTestHelper.Settle(() => viewer.VisualizeBitmap != null, "Visualize viewport");
            shown.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "DevelopControlsScrollViewer").Offset = default;
            var overlay = shown.GetVisualDescendants().OfType<SpotsOverlayControl>().Single();
            var pointer = overlay.TranslatePoint(new Point(overlay.Bounds.Width * .4, overlay.Bounds.Height * .4), shown)!.Value;
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(shown, pointer);
            Assert.True(overlay.IsVisible);
            Assert.True(shown.GetVisualDescendants().OfType<CompactSlider>().Single(s => s.Label == "Threshold").IsEffectivelyVisible);
            Assert.NotNull(viewer.VisualizeBitmap);
        });
    }
}

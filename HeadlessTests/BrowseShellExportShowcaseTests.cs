using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseShellExportShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("01-browse-dark", false, 1.5)]
    [InlineData("02-browse-selected", false, 1.5)]
    [InlineData("04-browse-menu", false, 1.5)]
    [InlineData("15-export", false, 1.5)]
    [InlineData("16-theme-menu", false, 1.5)]
    [InlineData("22-browse-gray", true, 1.5)]
    [InlineData("24-browse-100", false, 1)]
    [InlineData("title-dark", false, 1)]
    [InlineData("title-gray", true, 1)]
    [InlineData("status-dark", false, 1)]
    [InlineData("status-gray", true, 1)]
    [InlineData("footer-narrow-dark", false, 1)]
    [InlineData("footer-narrow-gray", true, 1)]
    [InlineData("filtered-loupe-dark", false, 1)]
    [InlineData("filtered-loupe-gray", true, 1)]
    public async Task RenderScene(string scene, bool gray, double scaling)
    {
        var narrow = scene.StartsWith("footer-");
        var size = narrow ? new PixelSize(800, 500) : new PixelSize(scaling == 1.5 ? 2202 : 2200, 1260);
        await DevelopToolsBaselineTests.WithScene("normal", size.Width, size.Height, async (vm, scope) =>
        {
            var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
            using var themeScope = new TestUiScope(theme: theme);
            vm.RestoreAppTheme(gray ? AppTheme.MidGray : AppTheme.Dark);
            var first = vm.SelectedImage!;
            using var thumbnail = new Bitmap(GoldenTestPaths.Asset("srgb-reference.jpg"));
            var images = Enumerable.Range(1, 6).Select(index => new ImageFile($"showcase-{index}.jpg")
            {
                Thumbnail = thumbnail,
                MetadataLoaded = true,
                PixelWidth = 6000,
                PixelHeight = 4000,
                Rating = index % 5,
                ColorLabel = (ColorLabel)(index % 5 + 1)
            }).Prepend(first).ToArray();
            first.Thumbnail = thumbnail;
            first.Rating = 4;
            first.IsRawJpegPair = true;
            first.BurstGroupOrdinal = 1;
            first.BurstIndex = 1;
            first.BurstSize = 3;
            first.GpsLatitude = 51.5074;
            first.GpsLongitude = -0.1278;
            first.MetadataLoaded = true;
            vm.Browse.SetImages(images);
            vm.SelectedImage = first;
            vm.SwitchToBrowseCommand.Execute(null);
            first.IsSelected = scene is "02-browse-selected" or "15-export";
            vm.RefreshSelectedCount();

            if (scene.StartsWith("filtered-loupe-"))
            {
                foreach (var image in images.Skip(1))
                {
                    image.Rating = 0;
                }

                vm.Browse.MinimumRating = 1;
                vm.EnterLoupeCommand.Execute(null);
                await vm.SetRatingCommand.ExecuteAsync(0);
                Assert.Empty(vm.Browse.VisibleImages);
                Assert.True(vm.IsLoupeMode);
            }

            if (scene == "15-export")
            {
                vm.ExportSettings.OutputFolder = Path.Combine(Path.GetTempPath(), "wp8-showcase-export");
                vm.ExportSettings.ExportWeb = true;
                await vm.SwitchToExportCommand.ExecuteAsync(null);
                await TestWaits.UntilAsync(() => vm.PreviewImage != null);
                vm.SelectedExportProofSize = vm.ExportProofSizes.Single(size => size.Name == "Web");
                vm.ExportSettings.ShowProof = true;
                await TestWaits.UntilAsync(() => vm.ExportProofCaption.StartsWith("Proof"));
            }

            if (scene.StartsWith("title-") || scene.StartsWith("status-"))
            {
                Control chrome = scene.StartsWith("title-")
                    ? new HappyPhotonTitleBar { DataContext = vm }
                    : new StatusBarView { DataContext = vm };
                if (chrome is StatusBarView status)
                {
                    status.FindControl<TextBlock>("StatusText")!.Text = "Ready to review";
                }

                ShowcaseTestHelper.Capture("wp8-" + scene, new Window { Content = chrome },
                    new PixelSize(1200, scene.StartsWith("title-") ? 40 : 24), theme);

                foreach (var image in images)
                {
                    image.Thumbnail = null;
                }

                return;
            }

            var window = scope.Window!;
            window.Width = size.Width / scaling;
            window.Height = size.Height / scaling;
            // test-teardown-policy: allow - WithScene owns this MainWindow scope.
            scope.Show();
            window.SetRenderScaling(scaling);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            ContextMenu? context = null;
            MenuFlyout? flyout = null;

            try
            {
                if (scene == "04-browse-menu")
                {
                    var tile = window.GetVisualDescendants().OfType<Border>()
                        .First(border => border.Name == "ThumbnailTile");
                    context = tile.ContextMenu!;
                    context.Placement = PlacementMode.Right;
                    context.Open(tile);
                    Assert.True(context.IsOpen);
                }

                if (scene == "16-theme-menu")
                {
                    var button = window.GetVisualDescendants().OfType<Button>()
                        .Single(control => control.Name == "AppearanceButton");
                    flyout = Assert.IsType<MenuFlyout>(button.Flyout);
                    flyout.ShowAt(button);
                    Assert.True(flyout.IsOpen);
                }

                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(size, frame.PixelSize);
                var directory = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots");
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, $"wp8-{scene}.png"));
            }
            finally
            {
                context?.Close();
                flyout?.Hide();

                foreach (var image in images)
                {
                    image.Thumbnail = null;
                }
            }
        });
    }
}

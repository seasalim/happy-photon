using Avalonia;
using Avalonia.Controls;
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

// VISUALS-WP10 after-captures: three selected tiles, the active photo outside the
// selection, and the pointer over an unselected or a selected tile.
public sealed class BrowseSelectionToneShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("tone-hover-dark", false, 5)]
    [InlineData("tone-hover-gray", true, 5)]
    [InlineData("tone-hover-selected-dark", false, 2)]
    [InlineData("tone-hover-selected-gray", true, 2)]
    public async Task RenderScene(string scene, bool gray, int hovered)
    {
        var size = new PixelSize(1600, 1000);
        await DevelopToolsBaselineTests.WithScene("normal", size.Width, size.Height, async (vm, scope) =>
        {
            var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
            using var themeScope = new TestUiScope(theme: theme);
            vm.RestoreAppTheme(gray ? AppTheme.MidGray : AppTheme.Dark);
            var first = vm.SelectedImage!;
            using var thumbnail = new Bitmap(GoldenTestPaths.Asset("srgb-reference.jpg"));
            var images = Enumerable.Range(1, 6).Select(index => new ImageFile($"tone-{index}.jpg")
            {
                Thumbnail = thumbnail,
                MetadataLoaded = true,
                PixelWidth = 6000,
                PixelHeight = 4000
            }).Prepend(first).ToArray();
            first.Thumbnail = thumbnail;
            first.MetadataLoaded = true;
            vm.Browse.SetImages(images);
            vm.SelectedImage = first;
            vm.SwitchToBrowseCommand.Execute(null);

            foreach (var image in images.Skip(1).Take(3))
            {
                image.IsSelected = true;
            }

            vm.RefreshSelectedCount();
            await Task.CompletedTask;

            var window = scope.Window!;
            window.Width = size.Width;
            window.Height = size.Height;
            // test-teardown-policy: allow - WithScene owns this MainWindow scope.
            scope.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            try
            {
                var tile = window.GetVisualDescendants().OfType<Border>()
                    .Single(border => border.Name == "ThumbnailTile" &&
                                      ReferenceEquals(border.DataContext, images[hovered]));
                window.MouseMove(tile.TranslatePoint(new Point(30, 30), window)!.Value);

                for (var tick = 0; tick < 20; tick++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }

                Assert.True(tile.IsPointerOver);
                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var directory = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots");
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, $"wp10-{scene}.png"), PngBitmapEncoderOptions.Default);
            }
            finally
            {
                foreach (var image in images)
                {
                    image.Thumbnail = null;
                }
            }
        });
    }
}

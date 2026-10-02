using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// SYNCSETTINGS G3 captures, after the mockup's scenes: 1 enabled button with its tooltip,
// 2 the first photo selected staying the source after eleven Ctrl+clicks, 3 the "⋯" menu,
// 4 the thumbnail menu on a selected photo, 5 the dialog opened by Sync, 7 Loupe with the button.
public sealed class SyncSettingsShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("1-enabled", false, 1600, 1000)]
    [InlineData("1-enabled", true, 1600, 1000)]
    [InlineData("1-enabled-800", false, 800, 500)]
    [InlineData("2-first-selected", false, 1600, 1000)]
    [InlineData("2-first-selected", true, 1600, 1000)]
    [InlineData("3-actions-menu", false, 1600, 1000)]
    [InlineData("3-actions-menu", true, 1600, 1000)]
    [InlineData("4-thumbnail-menu", false, 1600, 1000)]
    [InlineData("4-thumbnail-menu", true, 1600, 1000)]
    [InlineData("7-loupe", false, 1600, 1000)]
    [InlineData("7-loupe", true, 1600, 1000)]
    public async Task RenderScene(string scene, bool gray, int width, int height)
    {
        await DevelopToolsBaselineTests.WithScene("normal", width, height, async (vm, scope) =>
        {
            var theme = gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
            using var themeScope = new TestUiScope(theme: theme);
            vm.RestoreAppTheme(gray ? AppTheme.MidGray : AppTheme.Dark);
            var first = vm.SelectedImage!;
            using var thumbnail = new Bitmap(GoldenTestPaths.Asset("srgb-reference.jpg"));
            var images = Enumerable.Range(1, 11).Select(index => new ImageFile($"IMG_{1234 + index}.ARW")
            {
                Thumbnail = thumbnail,
                MetadataLoaded = true,
                PixelWidth = 6000,
                PixelHeight = 4000
            }).Prepend(first).ToArray();
            first.Thumbnail = thumbnail;
            vm.Browse.SetImages(images);
            vm.SelectedImage = first;
            vm.SwitchToBrowseCommand.Execute(null);
            vm.SelectAllCommand.Execute(null);

            if (scene == "2-first-selected")
            {
                // Mockup scene 2 (D-5): click the first photo, then Ctrl+click the
                // other eleven; the ring stays on the first, which is the source.
                vm.DeselectAllCommand.Execute(null);
                vm.Browse.SelectOnly(first);
                vm.SelectedImage = first;
                vm.RefreshSelectedCount();

                foreach (var image in images.Skip(1))
                {
                    vm.ToggleImageSelection(image);
                }

                Assert.Same(first, vm.SelectedImage);
                Assert.Equal(12, vm.SelectedCount);
            }

            if (scene == "7-loupe")
            {
                vm.EnterLoupeCommand.Execute(null);
                Assert.True(vm.IsLoupeMode);
            }

            await Task.CompletedTask;
            var window = scope.Window!;
            // test-teardown-policy: allow - WithScene owns this MainWindow scope.
            scope.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var button = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "SyncSettingsButton");
            Assert.True(button.IsEffectivelyEnabled);
            // The mockup's pane button: a filled, full-width compact button with a centred label.
            Assert.Contains("filled", button.Classes);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, button.HorizontalContentAlignment);
            ContextMenu? context = null;
            MenuFlyout? flyout = null;

            try
            {
                // Headless tooltips open at the window origin, so the captures show the text by assertion only.
                Assert.Equal("Sync settings from srgb-reference.jpg to 11 photos (Ctrl+Shift+S)",
                    ToolTip.GetTip(button));

                if (scene == "3-actions-menu")
                {
                    var actions = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "BrowseActionsButton");
                    flyout = Assert.IsType<MenuFlyout>(actions.Flyout);
                    flyout.ShowAt(actions);
                    Assert.True(flyout.IsOpen);
                }

                if (scene == "4-thumbnail-menu")
                {
                    var tile = window.GetVisualDescendants().OfType<Border>()
                        .Single(border => border.Name == "ThumbnailTile" && ReferenceEquals(border.DataContext, images[3]));
                    var point = tile.TranslatePoint(new Point(30, 30), window)!.Value;
                    window.MouseDown(point, MouseButton.Right, RawInputModifiers.None);
                    window.MouseUp(point, MouseButton.Right, RawInputModifiers.None);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Same(images[3], vm.SelectedImage);
                    Assert.Equal(12, vm.SelectedCount);
                    context = tile.ContextMenu!;
                    context.Close();
                    Dispatcher.UIThread.RunJobs();
                    context.Placement = PlacementMode.Right;
                    context.Open(tile);
                    Assert.True(context.IsOpen);
                }

                for (var tick = 0; tick < 10; tick++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }

                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var directory = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots");
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, $"syncsettings-{scene}-{(gray ? "gray" : "dark")}.png"));
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dialog(bool gray)
    {
        var vm = new PasteSettingsViewModel("IMG_1234.ARW", 11, new Dictionary<string, bool>(),
            mode: PasteSettingsMode.Sync);
        Assert.Equal("From IMG_1234.ARW to 11 photos", vm.Summary);
        var dialog = new PasteSettingsDialog(vm);

        ShowcaseTestHelper.Capture($"syncsettings-5-dialog-{(gray ? "gray" : "dark")}", dialog, new PixelSize(660, 572),
            gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
    }
}

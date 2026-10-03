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

public sealed class BatchUndoShowcaseTests
{
    [AvaloniaTheory]
    [InlineData(false, false, 1600, 1000)]
    [InlineData(true, false, 1600, 1000)]
    [InlineData(false, true, 1600, 1000)]
    [InlineData(true, true, 1600, 1000)]
    [InlineData(false, false, 800, 500)]
    [InlineData(true, false, 800, 500)]
    public async Task SceneEight(bool gray, bool restored, int width, int height)
    {
        await DevelopToolsBaselineTests.WithScene("normal", width, height, async (vm, scope) =>
        {
            using var themeScope = new TestUiScope(theme: gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
            vm.RestoreAppTheme(gray ? AppTheme.MidGray : AppTheme.Dark);
            vm.SwitchToBrowseCommand.Execute(null);
            var source = vm.SelectedImage!;
            vm.Exposure = 1;
            using var thumbnail = new Bitmap(GoldenTestPaths.Asset("srgb-reference.jpg"));
            var photos = Enumerable.Range(1, 11).Select(index => new ImageFile($"IMG_{1234 + index}.ARW")
            {
                Thumbnail = thumbnail,
                MetadataLoaded = true,
                PixelWidth = 6000,
                PixelHeight = 4000
            }).Prepend(source).ToArray();
            source.Thumbnail = thumbnail;
            vm.Browse.SetImages(photos);
            vm.SelectedImage = source;
            vm.SelectAllCommand.Execute(null);
            vm.ShowPasteSettingsAsync = _ => Task.FromResult(true);

            try
            {
                await vm.SyncSettingsCommand.ExecuteAsync(null);
                Assert.Equal("Undo sync (11 photos)", vm.UndoBatchText);
                if (restored) await vm.UndoBatchCommand.ExecuteAsync(null);

                // test-teardown-policy: allow - WithScene owns the supplied MainWindow scope.
                scope.Show();
                Dispatcher.UIThread.RunJobs();
                var window = scope.Window!;
                var button = window.GetVisualDescendants().OfType<Button>()
                    .Single(control => control.Name == "UndoSyncButton");
                Assert.Contains("compact-button", button.Classes);
                Assert.Contains("filled", button.Classes);
                Assert.Equal(!restored, button.IsEffectivelyVisible);
                Assert.Equal(restored ? "Restored 11 photos" : "Applied to 11 photos", vm.TransientStatus);

                for (var tick = 0; tick < 10; tick++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                }

                window.UpdateLayout();

                if (!restored)
                {
                    foreach (var ancestor in button.GetVisualAncestors())
                    {
                        var origin = button.TranslatePoint(default, ancestor);
                        Assert.NotNull(origin);
                        Assert.True(new Rect(ancestor.Bounds.Size).Contains(new Rect(origin.Value, button.Bounds.Size)),
                            $"Undo action is clipped by {ancestor.GetType().Name} at {width} × {height}.");
                    }
                }

                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(new PixelSize(width, height), frame.PixelSize);
                var directory = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots");
                Directory.CreateDirectory(directory);
                var suffix = width == 800 ? "-800x500" : "";
                frame.Save(Path.Combine(directory,
                    $"syncsettings-8-{(restored ? "restored" : "offered")}-{(gray ? "gray" : "dark")}{suffix}.png"));
            }
            finally
            {
                foreach (var photo in photos)
                {
                    photo.Thumbnail = null;
                }
            }
        });
    }
}

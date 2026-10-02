using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class FlatSquareShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("browse")]
    [InlineData("develop")]
    [InlineData("loupe")]
    [InlineData("compare")]
    [InlineData("export")]
    [InlineData("dropdown")]
    [InlineData("context-menu")]
    [InlineData("tooltip")]
    [InlineData("theme-menu")]
    [InlineData("settings")]
    public async Task RenderScene(string scene)
    {
        var size = new PixelSize(scene == "develop" ? 1600 : 1440, 900);
        await DevelopToolsBaselineTests.WithScene("normal", size.Width, size.Height, async (vm, scope) =>
        {
            using var theme = new TestUiScope(theme: ThemeVariant.Dark);
            vm.AppTheme = AppTheme.Dark;
            var first = vm.SelectedImage!;
            first.ColorLabel = ColorLabel.Red;
            first.Rating = 4;
            first.HasEdits = true;
            var path = GoldenTestPaths.Asset("display-p3-reference.jpg");
            Assert.Equal(0, (int)File.GetAttributes(path) & (0x1000 | 0x40000 | 0x400000));
            using var firstThumbnail = new Bitmap(first.FilePath);
            using var secondThumbnail = new Bitmap(path);
            first.Thumbnail = firstThumbnail;
            var second = new ImageFile(path) { Thumbnail = secondThumbnail, ColorLabel = ColorLabel.Blue };
            vm.Browse.SetImages([first, second]);
            vm.SwitchToBrowseCommand.Execute(null);
            vm.ToggleImageSelection(first);
            vm.ToggleImageSelection(second);

            if (scene == "develop") vm.SwitchToDevelopCommand.Execute(null);
            if (scene == "loupe") vm.ToggleLoupeCommand.Execute(null);
            if (scene == "compare") vm.ToggleCompareCommand.Execute(null);

            if (scene is "export" or "dropdown")
            {
                await vm.SwitchToExportCommand.ExecuteAsync(null);
            }

            ContextMenu? contextMenu = null;
            MenuFlyout? themeMenu = null;
            ComboBox? dropdown = null;
            Border? tooltipTarget = null;

            try
            {
                if (scene == "settings")
                {
                    ShowcaseTestHelper.Capture(scene, new SettingsDialog(vm), new PixelSize(650, 610),
                        ThemeVariant.Dark, dialog =>
                            Assert.Equal(0, dialog.FindControl<TabControl>("SettingsTabs")!.SelectedIndex));

                    return;
                }

                ShowcaseTestHelper.Capture(scene, scope, size, ThemeVariant.Dark, window =>
                {
                    if (scene is "browse" or "context-menu" or "tooltip")
                    {
                        var tile = window.GetVisualDescendants().OfType<Border>()
                            .Single(border => border.Name == "ThumbnailTile" && ReferenceEquals(border.DataContext, first));
                        Assert.True(first.IsSelected && first.IsActive);
                        Assert.Equal(default, tile.CornerRadius);

                        if (scene == "context-menu")
                        {
                            contextMenu = tile.ContextMenu!;
                            contextMenu.Placement = PlacementMode.Right;
                            contextMenu.Open(tile);
                            Assert.True(contextMenu.IsOpen);
                        }

                        if (scene == "tooltip")
                        {
                            tooltipTarget = tile;
                            ToolTip.SetPlacement(tile, PlacementMode.Bottom);
                            ToolTip.SetIsOpen(tile, true);
                            Assert.True(ToolTip.GetIsOpen(tile));
                        }
                    }

                    if (scene == "develop")
                    {
                        var labels = window.GetVisualDescendants().OfType<Button>()
                            .Where(button => button.Name == "ColorLabelButton" && button.IsEffectivelyVisible).ToArray();
                        Assert.NotEmpty(labels);

                        foreach (var button in labels)
                        {
                            var swatch = Assert.IsType<Border>(button.Content);
                            Assert.Equal(default, swatch.CornerRadius);
                            Assert.Equal(new Thickness(2), swatch.BorderThickness);

                            if (Equals(button.CommandParameter, ColorLabel.Red))
                            {
                                Assert.Equal(HappyPhotonColors.ControlActive, swatch.BorderBrush);
                            }
                        }

                        var mixer = window.GetVisualDescendants().OfType<MixerEditGroup>().Single();
                        mixer.BringIntoView();
                        var swatches = mixer.GetVisualDescendants().OfType<Button>()
                            .Where(button => button.Classes.Contains("mixer-band")).ToArray();
                        Assert.Equal(8, swatches.Length);
                        Assert.All(swatches, button =>
                        {
                            Assert.Equal(new CornerRadius(12), button.CornerRadius);
                            Assert.Equal(new CornerRadius(9), Assert.IsType<Border>(button.Content).CornerRadius);
                        });
                    }

                    if (scene == "dropdown")
                    {
                        dropdown = window.GetVisualDescendants().OfType<ComboBox>()
                            .Single(control => control.Name == "ExportFormatBox");
                        dropdown.IsDropDownOpen = true;
                        Assert.True(dropdown.IsDropDownOpen);
                    }

                    if (scene == "theme-menu")
                    {
                        var button = window.GetVisualDescendants().OfType<Button>()
                            .Single(control => control.Name == "AppearanceButton");
                        themeMenu = Assert.IsType<MenuFlyout>(button.Flyout);
                        themeMenu.ShowAt(button);
                        Assert.True(themeMenu.IsOpen);
                    }

                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                });
            }
            finally
            {
                contextMenu?.Close();
                themeMenu?.Hide();
                if (dropdown is not null) dropdown.IsDropDownOpen = false;
                if (tooltipTarget is not null) ToolTip.SetIsOpen(tooltipTarget, false);
                first.Thumbnail = null;
                second.Thumbnail = null;
            }
        });
    }
}

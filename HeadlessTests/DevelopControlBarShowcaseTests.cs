using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopControlBarShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("develop-bar-no-slider", 1000, 700, ImageFlag.Picked, 3)]
    [InlineData("develop-bar-overflow", 800, 600, ImageFlag.Rejected, 0)]
    public async Task RenderScene(string scene, int width, int height, ImageFlag flag, int rating)
    {
        await DevelopToolsBaselineTests.WithScene("normal", width, height, (vm, scope) =>
        {
            vm.SelectedImage!.Flag = flag;
            vm.SelectedImage.Rating = rating;
            var overflow = scene == "develop-bar-overflow";

            // Popups have a separate headless render root; menu-open behavior is covered by the menu tests.
            ShowcaseTestHelper.Capture(scene, scope, new PixelSize(width, height),
                overflow ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, window =>
                {
                    var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
                    window.UpdateLayout();
                    Assert.False(pane.FindControl<CompactSlider>("DevelopZoomSlider")!.IsVisible);
                    Assert.Equal(overflow, pane.FindControl<Button>("DevelopViewActionsButton")!.IsVisible);
                });

            return Task.CompletedTask;
        });
    }
}

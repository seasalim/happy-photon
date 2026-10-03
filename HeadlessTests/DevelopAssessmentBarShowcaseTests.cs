using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopAssessmentBarShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("develop-bar-full", 1600, 900, ImageFlag.Picked, 3, ColorLabel.Red)]
    [InlineData("develop-bar-compact", 1100, 600, ImageFlag.Rejected, 5, ColorLabel.Blue)]
    [InlineData("develop-bar-empty", 1200, 900, ImageFlag.Unflagged, 0, ColorLabel.None)]
    public async Task RenderScene(string scene, int width, int height, ImageFlag flag, int rating, ColorLabel label)
    {
        await DevelopToolsBaselineTests.WithScene("normal", width, height, (vm, scope) =>
        {
            vm.SelectedImage!.Flag = flag;
            vm.SelectedImage.Rating = rating;
            vm.SelectedImage.ColorLabel = label;

            ShowcaseTestHelper.Capture(scene, scope, new PixelSize(width, height),
                scene == "develop-bar-compact" ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, window =>
                {
                    var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
                    var workspace = pane.GetVisualAncestors().OfType<Grid>()
                        .Single(grid => grid.ColumnDefinitions.Count == 5);

                    // Full and compact use default panes; widen both panes (within their maximums) for the empty tier.
                    if (scene == "develop-bar-empty")
                    {
                        workspace.ColumnDefinitions[0].Width = new GridLength(400);
                        workspace.ColumnDefinitions[4].Width = new GridLength(388);
                    }

                    window.UpdateLayout();
                    Assert.Equal(scene == "develop-bar-full", pane.FindControl<Border>("DevelopAssessmentHost")!.IsVisible);
                    Assert.Equal(scene == "develop-bar-compact", pane.FindControl<DevelopAssessmentCluster>("DevelopCompactAssessment")!.IsVisible);
                });

            return Task.CompletedTask;
        });
    }
}

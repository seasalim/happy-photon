using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ShootingInfoNavigatorShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("develop-shooting-info")]
    [InlineData("develop-navigator-zoom")]
    [InlineData("shell-panes-at-max")]
    public async Task RenderScene(string scene)
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1440, 900, (vm, scope) =>
        {
            ShootingInfoNavigatorTests.SetExif(vm.SelectedImage!);
            var manual = scene == "develop-navigator-zoom";
            var theme = manual ? HappyPhotonThemes.MidGray : ThemeVariant.Dark;
            ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1440, 900),
                theme, window =>
                {
                    if (scene == "shell-panes-at-max")
                    {
                        ShellPaneLimitsTests.ExpandPanes(window);
                    }

                    if (manual)
                    {
                        vm.ManualZoomLevel = 1.5;
                    }
                    else
                    {
                        vm.ZoomFitCommand!.Execute(null);
                    }

                    ShellPaneLimitsTests.Settle(window);
                    window.FindControl<DevelopEditPanel>("DevelopEditPanel")!
                        .FindControl<ScrollViewer>("DevelopControlsScrollViewer")!.Offset = default;

                    // Refresh text after intermediate headless captures without changing the scene theme.
                    foreach (var text in window.GetVisualDescendants().OfType<TextBlock>())
                    {
                        text.InvalidateVisual();
                    }

                    window.UpdateLayout();
                    Assert.Equal(theme, window.ActualThemeVariant);
                    var readout = window.FindControl<TextBlock>("NavigatorZoomReadout")!;
                    var muted = ThemeResourceTests.Brush("TextMuted", theme).Color;
                    Assert.Equal(muted, Assert.IsAssignableFrom<ISolidColorBrush>(readout.Foreground).Color);
                    var row = window.FindControl<DevelopEditPanel>("DevelopEditPanel")!
                        .FindControl<Grid>("ShootingInfoRow")!;
                    Assert.All(row.Children.Cast<TextBlock>(), cell =>
                        Assert.Equal(muted, Assert.IsAssignableFrom<ISolidColorBrush>(cell.Foreground).Color));
                    Assert.Equal(manual ? string.Format("{0:P0}", 1.5) : "Fit",
                        readout.Text);
                });

            return Task.CompletedTask;
        });
    }
}

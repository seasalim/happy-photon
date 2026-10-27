using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class SignedReadoutBaselineTests
{
    [AvaloniaTheory]
    [InlineData("signed-readouts-values", false)]
    [InlineData("signed-readouts-values-before", true)]
    public async Task CaptureMixedValuesShowcase(string scene, bool baseFormats)
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1200, 700, async (vm, scope) =>
        {
            vm.Exposure = .35;
            vm.Contrast = 15;
            vm.Highlights = -40;
            vm.Shadows = 20;
            vm.Whites = -5;
            vm.Blacks = 0;
            vm.Texture = 10;
            vm.Clarity = -12;

            if (vm.PendingPreviewDebounceTask is { } pending)
            {
                await pending.WaitAsync(TestWaits.Condition);
            }

            await TestWaits.UntilAsync(() => vm.CaptureBackgroundActivitySnapshot().PreviewCount == 0
                && !vm.IsBackgroundActivityStatusVisible);
            ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700), ThemeVariant.Dark, window =>
            {
                var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
                var groups = panel.GetVisualDescendants().OfType<DevelopGroup>()
                    .Where(group => group.Header is "Adjustments" or "Presence").ToArray();
                Assert.Equal(2, groups.Length);
                var sliders = groups.SelectMany(group => group.GetVisualDescendants().OfType<CompactSlider>())
                    .ToArray();
                Assert.Equal(11, sliders.Length);

                if (baseFormats)
                {
                    // Reproduce 3765a9d's formats only on this scene's disposable controls.
                    foreach (var slider in sliders)
                    {
                        slider.StringFormat = slider.Label == "Exposure" ? "{0:F2}" : "{0:0}";
                    }
                }

                var scroll = panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!;
                scroll.Offset = new Vector(0, 240);
                window.UpdateLayout();
                ShowcaseTestHelper.SettleExpanderChevrons(panel);

                foreach (var slider in sliders)
                {
                    var text = slider.FindControl<TextBlock>("ValueText")!;
                    var magnitude = Math.Abs(slider.Value).ToString(slider.Label == "Exposure" ? "F2" : "F0");
                    var sign = slider.Value < 0 ? "-" : slider.Value > 0 && !baseFormats ? "+" : "";
                    Assert.Equal(sign + magnitude, text.Text);
                    Assert.True(slider.IsEffectivelyVisible);
                    var top = slider.TranslatePoint(default, scroll)!.Value.Y;
                    Assert.InRange(top, 0, scroll.Viewport.Height - slider.Bounds.Height);
                }
            });
        });
    }

    [AvaloniaFact]
    public async Task CaptureAdjustmentsShowcase()
    {
        await new DevelopGroupShowcaseTests().DevelopGroupsRenderAtEachScrollPosition(
            "signed-readouts-adjustments", 240);
    }
}

using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// Retain the measurements alongside assertions for reproducible readouts and fit.
public sealed class SignedReadoutBaselineTests(ITestOutputHelper output)
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

    [AvaloniaFact]
    public async Task MeasureGlobalReadoutsAndSignedTextAtMinimumPaneWidth()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(root.Path, "catalog"));
        await using var vm = new MainWindowViewModel(
            catalog, new NullBaseLoader(), _ => Task.CompletedTask);
        vm.IsDevelopMode = true;
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 200, Height = 700, Content = panel };
        using var scope = new TestUiScope(window, ThemeVariant.Dark);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(200, panel.Bounds.Width);
        var global = panel.GetVisualDescendants().OfType<StackPanel>()
            .Single(control => control.Classes.Contains("global-edits"));
        var sliders = global.GetVisualDescendants().OfType<CompactSlider>()
            .Where(slider => slider.Minimum < 0 && slider.Maximum == -slider.Minimum)
            .ToArray();
        Assert.Equal(20, sliders.Length);
        output.WriteLine($"ENV culture={CultureInfo.CurrentCulture.Name}; " +
            $"pane={panel.Bounds.Width}; scale={window.RenderScaling}; sliders={sliders.Length}");

        foreach (var slider in sliders)
        {
            var group = slider.GetVisualAncestors().OfType<DevelopGroup>().First();
            var name = $"{group.Header}/{slider.Label}";
            var text = slider.FindControl<TextBlock>("ValueText")!;
            var readings = new List<string>();
            var positive = slider.Label == "Exposure" ? .35 : 15;

            foreach (var value in new[] { positive, 0, -positive })
            {
                slider.SetCurrentValue(CompactSlider.ValueProperty, value);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(value, slider.Value);
                var magnitude = Math.Abs(value).ToString(slider.Label == "Exposure" ? "F2" : "F0");
                var expected = value > 0 ? "+" + magnitude : value < 0 ? "-" + magnitude : magnitude;
                Assert.Equal(expected, text.Text);
                readings.Add($"{value.ToString(CultureInfo.InvariantCulture)}='{text.Text}'");
            }

            output.WriteLine($"A1 {name}: {string.Join("; ", readings)}");
            var column = slider.FindControl<Grid>("LayoutGrid")!.ColumnDefinitions[2].ActualWidth;
            Assert.Equal(40, column);
            var candidates = slider.Label == "Exposure"
                ? new[] { "+3.00", "-3.00" }
                : new[] { "+100", "-100" };

            foreach (var candidate in candidates)
            {
                var probe = new TextBlock
                {
                    Text = candidate,
                    FontFamily = text.FontFamily,
                    FontSize = text.FontSize,
                    FontStyle = text.FontStyle,
                    FontWeight = text.FontWeight,
                    FontStretch = text.FontStretch,
                    LetterSpacing = text.LetterSpacing
                };
                probe.Measure(Size.Infinity);
                Assert.True(probe.DesiredSize.Width <= column, $"{name}: '{candidate}' exceeds {column}px.");
                Assert.Equal(TextTrimming.None, text.TextTrimming);
                output.WriteLine(FormattableString.Invariant(
                    $"A2 {name}: text='{candidate}'; intrinsic={probe.DesiredSize.Width:R}; column={column:R}; row={slider.Bounds.Width:R}; trimming={text.TextTrimming}; font={text.FontFamily}; size={text.FontSize:R}"));
            }
        }
    }
}

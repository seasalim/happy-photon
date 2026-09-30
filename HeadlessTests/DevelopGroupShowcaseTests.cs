using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopGroupShowcaseTests
{
    [AvaloniaTheory]
    [InlineData("develop-groups", 0)]
    [InlineData("develop-groups-middle", 440)]
    [InlineData("develop-groups-lower", 820)]
    [InlineData("develop-groups-optics", 1200)]
    public async Task DevelopGroupsRenderAtEachScrollPosition(string scene, double offset)
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1200, 700, (_, scope) =>
        {
            ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700), ThemeVariant.Dark, window =>
            {
                var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
                var groups = panel.GetVisualDescendants().OfType<DevelopGroup>().ToArray();
                Assert.Equal(10, groups.Length);
                Assert.All(groups, group => Assert.True(group.IsExpanded));
                panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!.Offset = new Vector(0, offset);
                window.UpdateLayout();
                ShowcaseTestHelper.SettleExpanderChevrons(panel);
            });

            return Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData("presets-restyle", false)]
    [InlineData("presets-restyle-midgray", true)]
    public async Task PresetCategoriesRenderInBothThemes(string scene, bool gray)
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1200, 700, (vm, scope) =>
        {
            vm.RestorePresetGroups(new Dictionary<string, bool>
            {
                ["My Presets"] = true, ["Natural"] = true, ["Portrait"] = false,
                ["Landscape"] = false, ["Black & White"] = false, ["Creative"] = false
            });
            ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1200, 700),
                gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, window =>
                {
                    var presets = window.GetVisualDescendants().OfType<PresetsPanel>().Single();
                    var categories = presets.GetVisualDescendants().OfType<Expander>().ToArray();
                    Assert.Equal(6, categories.Length);
                    ShowcaseTestHelper.SettleExpanderChevrons(window);

                    foreach (var category in categories)
                    {
                        var chevron = category.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                            .Single(path => path.Name == "ExpandCollapseChevron");
                        Assert.Equal(new Size(10, 6), chevron.Bounds.Size);
                        Assert.Equal(1.5, chevron.StrokeThickness);
                        Assert.Equal(Stretch.Fill, chevron.Stretch);
                        Assert.Equal(PenLineCap.Round, chevron.StrokeLineCap);
                        Assert.Equal(PenLineJoin.Round, chevron.StrokeJoin);
                        Assert.Equal(ThemeResourceTests.Brush("TextMuted", window.ActualThemeVariant).Color,
                            Assert.IsAssignableFrom<ISolidColorBrush>(chevron.Stroke).Color);
                    }
                });

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task HeadersIgnorePointerAndKeyboardInput()
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1200, 700, (_, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns and disposes this MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            Dispatcher.UIThread.RunJobs();
            var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
            var groups = panel.GetVisualDescendants().OfType<DevelopGroup>().ToArray();

            foreach (var group in groups)
            {
                var header = group.GetVisualDescendants().OfType<ToggleButton>()
                    .Single(c => c.Name == "ExpanderHeader");
                header.BringIntoView();
                window.UpdateLayout();
                var point = header.TranslatePoint(new Point(20, 8), window)!.Value;

                foreach (var modifiers in new[] { RawInputModifiers.None, RawInputModifiers.Alt })
                {
                    window.MouseDown(point, MouseButton.Left, modifiers);
                    window.MouseUp(point, MouseButton.Left, modifiers);
                    Assert.All(groups, item => Assert.True(item.IsExpanded));
                }

                Assert.False(header.Focus());
                panel.Focusable = true;
                Assert.True(panel.Focus());

                foreach (var key in new[] { Key.Enter, Key.Space })
                {
                    var physicalKey = key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Space;
                    window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
                    window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
                    Assert.True(group.IsExpanded);
                    Assert.True(header.IsChecked);
                }
            }

            return Task.CompletedTask;
        });
    }
}

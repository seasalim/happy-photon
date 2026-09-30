using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// Owner-approved WP1 gates; the original readings are retained in the work-package spec.
public sealed class DevelopHeaderBaselineTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task MeasureUnifiedLayout()
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1200, 700, async (vm, scope) =>
        {
            using var theme = new TestUiScope(theme: Avalonia.Styling.ThemeVariant.Dark);
            // test-teardown-policy: allow - WithScene owns and disposes this MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            Settle(window);
            var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
            var global = panel.GetVisualDescendants().OfType<StackPanel>()
                .Single(c => c.Classes.Contains("global-edits"));
            var groups = ReadGroups(global);
            MeasureVisibleGaps(global, groups);
            output.WriteLine($"SCENE window={window.Bounds} panel={panel.Bounds} theme={window.ActualThemeVariant}");
            // Above: previous content bottom to title top; first starts at global origin.
            // Below: title bottom to first content top. Content excludes outer margins.
            // L1 uses the curve card edge; L2 retains the baseline canvas/footer span.
            double[] expectedHeight = [44, 84, 246, 44, 130, 104, 68, 106, 92, 114];
            var previousBottom = 0d;
            var spacingSum = 0d;

            foreach (var group in groups)
            {
                var title = BoundsIn(group.Title, global);
                var first = BoundsIn(group.Content[0], global);
                var last = BoundsIn(group.Content[^1], global);
                var layoutContent = BoundsIn((Control)((DevelopGroup)group.Owner).Content!, global);
                var above = title.Top - previousBottom;
                var below = layoutContent.Top - title.Bottom;
                Assert.InRange(above, 18.5, 19.5);
                Assert.InRange(below, 7.5, 8.5);
                var index = Array.IndexOf(groups, group);
                Assert.InRange(last.Bottom - first.Top, expectedHeight[index] - .5, expectedHeight[index] + .5);
                spacingSum += above + below;
                output.WriteLine(FormattableString.Invariant(
                    $"L1 {group.Name}: above={above:R}; below={below:R}; title={title}; titleMargin={group.Title.Margin}"));
                output.WriteLine(FormattableString.Invariant(
                    $"L2 {group.Name}: contentHeight={last.Bottom - first.Top:R}; contentTop={first.Top:R}; contentBottom={last.Bottom:R}; ownerHeight={group.Owner.Bounds.Height:R}"));
                previousBottom = layoutContent.Bottom;
            }

            var curve = global.GetVisualDescendants().OfType<CurveView>().Single();
            var canvas = curve.FindControl<Canvas>("CurveCanvas")!;
            Assert.Equal(new Size(198, 111), canvas.Bounds.Size);
            Assert.Equal(180, curve.Bounds.Height);
            output.WriteLine($"L2 CurveCanvas: parentBounds={canvas.Bounds}; inCurve={BoundsIn(canvas, curve)}; cardHeight={curve.Bounds.Height}");
            var scroll = panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!;
            Assert.InRange(scroll.Extent.Height, 0, 1532.5);
            output.WriteLine(FormattableString.Invariant(
                $"L3 extent={scroll.Extent}; viewport={scroll.Viewport}; spacingSum={spacingSum:R}; normalizationDelta={270 - spacingSum:R}; thresholdWithoutChevronRows={scroll.Extent.Height + 270 - spacingSum:R}"));
            MeasurePresets(window, "Dark");
            MeasureHeaders(groups);
            MeasureEnabled(global, "normal");
            MeasureFocus(window, global, "normal");
            await vm.ToggleCropModeCommand.ExecuteAsync(null);
            Settle(window);
            Assert.True(vm.IsToolActive);
            var saturation = panel.FindControl<CompactSlider>("SaturationSlider")!;
            MeasureOpacity(saturation, "SaturationSlider");
            MeasureOpacity(saturation.FindControl<TextBlock>("LabelText")!, "SaturationSlider.LabelText");
            MeasureOpacity(curve.FindControl<Button>("RedChannelButton")!, "RedChannelButton");
            foreach (var control in global.GetVisualDescendants().OfType<Control>()
                .Where(c => c.Classes.Any(name => name is "color-dependent" or "crossing-dependent" or
                    "raw-profile-row" or "midpoint-row" or "optics-group" or "optics-row")))
            {
                MeasureOpacity(control, control.Name ?? control.Classes.First());
            }

            MeasureEnabled(global, "crop");
            MeasureFocus(window, global, "crop");

            using var gray = new TestUiScope(theme: HappyPhotonThemes.MidGray);
            Settle(window);
            MeasurePresets(window, "MidGray");
        });
    }

    private static Group[] ReadGroups(StackPanel global)
    {
        var groups = global.Children.Cast<DevelopGroup>().ToArray();
        Assert.Equal(new[] { "Profile", "White Balance", "Adjustments", "Presence", "Tone Curve",
            "Color Mixer", "Detail", "Effects", "Geometry", "Optics" }, groups.Select(g => g.Header));

        return groups.Select(group =>
        {
            var title = group.GetVisualDescendants().OfType<TextBlock>()
                .Single(c => c.Classes.Contains("section-label"));
            Assert.Equal(12, title.FontSize);
            Assert.Equal(0, title.LetterSpacing);
            var content = (Control)group.Content!;
            var controls = content is CurveView curve
                ? curve.GetVisualDescendants().OfType<Grid>().Single(c => c.RowDefinitions.Count == 3)
                    .Children.Skip(1).ToArray()
                : (content as StackPanel ?? (StackPanel)((UserControl)content).Content!)
                    .Children.Where(c => c.IsEffectivelyVisible).ToArray();
            Assert.DoesNotContain(content.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Classes.Contains("section-label"));
            var divider = group.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "GroupDivider");
            Assert.Equal(group != groups[0], divider.IsVisible);

            return new Group((string)group.Header!, group, title, controls);
        }).ToArray();
    }

    private void MeasureVisibleGaps(StackPanel global, Group[] groups)
    {
        // Observe visible control bounds, excluding empty text and unpainted layout rows.
        // Optics is last, so report its trailing content edge instead of a nonexistent divider.
        foreach (var group in groups)
        {
            var content = (Control)((DevelopGroup)group.Owner).Content!;
            var visible = content.GetVisualDescendants().OfType<Control>()
                .Prepend(content)
                .Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0)
                .Where(c => c is CompactSlider or ComboBox or ListBox or CheckBox or CurveView ||
                    c is TextBlock text && !string.IsNullOrWhiteSpace(text.Text))
                .OrderBy(c => BoundsIn(c, global).Bottom).Last();
            var index = Array.IndexOf(groups, group);
            var nextDivider = index + 1 < groups.Length
                ? groups[index + 1].Owner.GetVisualDescendants().OfType<Border>()
                    .Single(c => c.Name == "GroupDivider")
                : null;
            var end = nextDivider is null ? BoundsIn(content, global).Bottom : BoundsIn(nextDivider, global).Top;
            var gap = end - BoundsIn(visible, global).Bottom;
            output.WriteLine(FormattableString.Invariant(
                $"L1 {group.Name}: visibleGap={gap:R}; lastVisible={Identity(visible)}; end={(nextDivider is null ? "content end (no following divider)" : "next divider")}"));
        }

    }

    private void MeasurePresets(Window window, string theme)
    {
        var presets = window.GetVisualDescendants().OfType<PresetsPanel>().Single();
        var headers = presets.GetVisualDescendants().OfType<TextBlock>()
            .Where(c => c.Classes.Contains("preset-header")).ToArray();
        Assert.NotEmpty(headers);

        foreach (var header in headers)
        {
            Assert.True(header.TryFindResource("TextMuted", header.ActualThemeVariant, out var primary));
            Assert.Equal(11, header.FontSize);
            Assert.Equal(Avalonia.Media.FontWeight.Medium, header.FontWeight);
            Assert.Same(primary, header.Foreground);
            output.WriteLine($"L4 {theme} {header.Text}: size={header.FontSize}; weight={header.FontWeight}({(int)header.FontWeight}); foreground={header.Foreground}; sameTextMuted={ReferenceEquals(primary, header.Foreground)}");
        }

        var rows = presets.GetVisualDescendants().OfType<Button>()
            .Where(c => c.Classes.Contains("preset") && c.IsEffectivelyVisible).ToArray();
        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.Equal(24, row.Bounds.Height);
            Assert.True(row.TryFindResource("TextPrimary", row.ActualThemeVariant, out var primary));
            Assert.Same(primary, row.Foreground);
        });
        output.WriteLine($"L4 {theme} presetRows={rows.Length}; arrangedHeights={string.Join(",", rows.Select(c => c.Bounds.Height).Distinct())}");
    }

    private void MeasureHeaders(Group[] groups)
    {
        foreach (var group in groups)
        {
            var expander = (DevelopGroup)group.Owner;
            var header = expander.GetVisualDescendants().OfType<ToggleButton>()
                .Single(c => c.Name == "ExpanderHeader");
            var chevron = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                .Single(c => c.Name == "ExpandCollapseChevron");
            Assert.Equal(new Size(10, 6), chevron.Bounds.Size);
            Assert.Equal(1.5, chevron.StrokeThickness);
            Assert.Equal(Avalonia.Media.Stretch.Fill, chevron.Stretch);
            Assert.Equal(Avalonia.Media.PenLineCap.Round, chevron.StrokeLineCap);
            Assert.Equal(Avalonia.Media.PenLineJoin.Round, chevron.StrokeJoin);
            var title = BoundsIn(group.Title, expander);
            var glyph = BoundsIn(chevron, expander);
            Assert.InRange(glyph.Center.Y, title.Top, title.Bottom);
            output.WriteLine($"CHEVRON {group.Name}: size={chevron.Bounds.Size}; stroke={chevron.StrokeThickness}");

            Assert.Equal(group.Name, AutomationProperties.GetName(header));
            Assert.False(header.Focusable);
            Assert.False(header.IsTabStop);
            Assert.False(header.IsHitTestVisible);
            Assert.False(header.Focus());
            expander.IsExpanded = false;
            Assert.True(expander.IsExpanded);
            output.WriteLine($"L6 {group.Name}: expanded={expander.IsExpanded}; focusable={header.Focusable}; hitTestVisible={header.IsHitTestVisible}");
        }
    }

    private void MeasureEnabled(StackPanel global, string mode)
    {
        var controls = global.GetVisualDescendants().OfType<Control>()
            .Where(c => c is CompactSlider or Button or ComboBox or ListBox)
            .Where(c => c.Name != "ExpanderHeader")
            .Where(c => c.IsEffectivelyVisible).ToArray();
        Assert.All(global.Children.Cast<DevelopGroup>(), group =>
        {
            var header = group.GetVisualDescendants().OfType<ToggleButton>()
                .Single(c => c.Name == "ExpanderHeader");
            Assert.Equal(mode == "normal", header.IsEffectivelyEnabled);
        });
        Assert.Equal(48, controls.Length);
        Assert.Equal(mode == "normal" ? 42 : 0, controls.Count(c => c.IsEffectivelyEnabled));
        output.WriteLine($"L6 {mode}: globalEnabled={global.IsEffectivelyEnabled}; controls={controls.Length}; enabled={controls.Count(c => c.IsEffectivelyEnabled)}");
        output.WriteLine($"L6 {mode} disabled: {string.Join(" -> ", controls.Where(c => !c.IsEffectivelyEnabled).Select(Identity))}");
    }

    private void MeasureFocus(Window window, StackPanel global, string mode)
    {
        // Start before the group area; send actual Tab events for one full cycle.
        var start = window.GetVisualDescendants().OfType<ToggleButton>()
            .Single(c => c.Name == "LocalsModeButton");
        Assert.True(start.Focus());
        var seen = new HashSet<Control>();
        var sequence = new List<string>();
        var completedCycle = false;

        for (var i = 0; i < 400; i++)
        {
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Dispatcher.UIThread.RunJobs();
            var focused = window.FocusManager!.GetFocusedElement() as Control;
            if (focused is null) continue;

            if (!seen.Add(focused))
            {
                completedCycle = true;
                break;
            }

            if (focused.GetVisualAncestors().Contains(global))
            {
                sequence.Add(Identity(focused));
            }
        }

        Assert.True(completedCycle, "Keyboard traversal must complete a cycle within 400 Tab events.");
        var expected = mode == "normal" ? new[]
        {
            "WhiteBalanceModeBox", "WhiteBalanceAutoButton", "WhiteBalancePickerButton",
            "SliderRoot[Kelvin]", "SliderRoot[Tint]", "SliderRoot[Exposure]", "BrightnessSlider[Brightness]",
            "SliderRoot[Contrast]", "SaturationSlider[Saturation]", "VibranceSlider[Vibrance]",
            "SliderRoot[Shadows]", "SliderRoot[Highlights]", "SliderRoot[Whites]", "SliderRoot[Blacks]",
            "TextureSlider[Texture]", "ClaritySlider[Clarity]", "CompositeChannelButton", "RedChannelButton",
            "GreenChannelButton", "BlueChannelButton", "Reset curve", "RedMixerButton", "OrangeMixerButton",
            "YellowMixerButton", "GreenMixerButton", "AquaMixerButton", "BlueMixerButton", "PurpleMixerButton",
            "MagentaMixerButton", "MixerHueSlider[Hue]", "MixerSaturationSlider[Saturation]",
            "MixerLuminanceSlider[Luminance]", "CaptureSharpenSlider[Sharpen]", "LuminanceNrSlider[Luma NR]",
            "ChromaNrSlider[Chroma NR]", "VignetteSlider[Vignette]", "GrainSlider[Grain]", "ListBoxItem[Medium]",
            "GeometryVerticalSlider[Vertical]", "GeometryHorizontalSlider[Horizontal]",
            "GeometryAspectSlider[Aspect]", "GeometryDistortionSlider[Distortion]"
        } : Array.Empty<string>();
        Assert.Equal(expected, sequence);
        output.WriteLine($"L6 {mode} tabOrder ({sequence.Count}): {string.Join(" -> ", sequence)}");
    }

    private void MeasureOpacity(Control control, string name)
    {
        var visuals = new[] { (Visual)control }.Concat(control.GetVisualAncestors()).ToArray();
        var effective = visuals.Aggregate(1d, (value, visual) => value * visual.Opacity);
        Assert.True(control.TryFindResource("DisabledOpacity", control.ActualThemeVariant, out var disabled));
        Assert.Equal((double)disabled!, effective, 6);
        output.WriteLine(FormattableString.Invariant(
            $"L5 {name}: effective={effective:R}; local={control.Opacity:R}; DisabledOpacity={disabled}; factors={string.Join(",", visuals.Where(c => c.Opacity != 1).Select(c => c.GetType().Name + ":" + c.Opacity.ToString("R", CultureInfo.InvariantCulture)))}"));
    }

    private static string Identity(Control control) => control switch
    {
        CompactSlider slider => $"{slider.Name ?? slider.Label}[{slider.Label}]",
        _ => control.Name ?? AutomationProperties.GetName(control) ??
            (control is ContentControl content ? $"{control.GetType().Name}[{content.Content}]" : control.GetType().Name)
    };

    private static Rect BoundsIn(Control control, Visual relative)
    {
        var origin = control.TranslatePoint(default, relative)!.Value;

        return new Rect(origin, control.Bounds.Size);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }

    private sealed record Group(string Name, Control Owner, TextBlock Title, Control[] Content);
}



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
            // WP2 moves two 24px slider rows to Presence; Recovery now ends Adjustments.
            // The Profile picker adds 5px above its status line and 8px below it.
            double[] expectedHeight = [47, 84, 192, 92, 128, 104, 68, 104, 92, 114];
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
                Assert.InRange(above, previousBottom == 0 ? 7.5 : 11.5, previousBottom == 0 ? 8.5 : 12.5);
                Assert.InRange(below, 15.5, 16.5);
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
            Assert.Equal(new Size(198, 109), canvas.Bounds.Size);
            Assert.Equal(180, curve.Bounds.Height);
            output.WriteLine($"L2 CurveCanvas: parentBounds={canvas.Bounds}; inCurve={BoundsIn(canvas, curve)}; cardHeight={curve.Bounds.Height}");
            var scroll = panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!;
            var normalExtent = scroll.Extent.Height;
            output.WriteLine($"WP11 extent delta from 1534: {scroll.Extent.Height - 1534}");
            output.WriteLine(FormattableString.Invariant(
                $"L3 extent={scroll.Extent}; viewport={scroll.Viewport}; spacingSum={spacingSum:R}; normalizationDelta={270 - spacingSum:R}; thresholdWithoutChevronRows={scroll.Extent.Height + 270 - spacingSum:R}"));
            MeasurePresets(window, "Dark");
            MeasureHeaders(groups);
            MeasureEnabled(global, "normal");
            MeasureFocus(window, global, "normal");
            await vm.ToggleCropModeCommand.ExecuteAsync(null);
            Settle(window);
            Assert.True(vm.IsToolActive);
            var saturation = panel.FindControl<PresenceEditGroup>("PresenceEditGroup")!.FindControl<CompactSlider>("SaturationSlider")!;
            MeasureOpacity(saturation, "SaturationSlider");
            MeasureOpacity(saturation.FindControl<TextBlock>("LabelText")!, "SaturationSlider.LabelText");
            MeasureOpacity(curve.FindControl<ListBoxItem>("RedChannelButton")!, "RedChannelButton");
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
            // FIXES-DEVELOP-WP11 G7: owner-accepted at 1544 (+10 from 1534: 32 px blocks with 8/4 content spacing replace 43 px headers).
            Assert.InRange(normalExtent, 1543.5, 1544.5);
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
            Assert.DoesNotContain(group.GetVisualDescendants().OfType<Border>(), c => c.Name == "GroupDivider");

            return new Group((string)group.Header!, group, title, controls);
        }).ToArray();
    }

    private void MeasureVisibleGaps(StackPanel global, Group[] groups)
    {
        // Observe visible control bounds, excluding empty text and unpainted layout rows.
        // Optics is last, so report its trailing content edge instead of a nonexistent next header.
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
            var nextHeader = index + 1 < groups.Length
                ? groups[index + 1].Owner
                : null;
            var end = nextHeader is null ? BoundsIn(content, global).Bottom : BoundsIn(nextHeader, global).Top;
            var gap = end - BoundsIn(visible, global).Bottom;
            output.WriteLine(FormattableString.Invariant(
                $"L1 {group.Name}: visibleGap={gap:R}; lastVisible={Identity(visible)}; end={(nextHeader is null ? "content end (no following header)" : "next header")}"));
        }

    }

    private void MeasurePresets(Window window, string theme)
    {
        var presets = window.GetVisualDescendants().OfType<PresetsPanel>().Single();
        var headers = presets.GetVisualDescendants().OfType<Expander>()
            .SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>())
            .Where(c => c.Classes.Contains("section-label")).ToArray();
        Assert.NotEmpty(headers);

        foreach (var header in headers)
        {
            Assert.True(header.TryFindResource("TextMuted", header.ActualThemeVariant, out var primary));
            Assert.Equal(12, header.FontSize);
            Assert.Equal(Avalonia.Media.FontWeight.SemiBold, header.FontWeight);
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
            AssertDisclosureTriangle(chevron, expander.IsExpanded);
            var title = BoundsIn(group.Title, expander);
            var glyph = BoundsIn(chevron, expander);
            var fill = header.GetVisualDescendants().OfType<Border>()
                .Single(c => c.Name == "ToggleButtonBackground");
            output.WriteLine($"HEADER-FILL {group.Name}: bounds={BoundsIn(fill, expander)}; contentWidth={((Control)expander.Content!).Bounds.Width}; radius={fill.CornerRadius}");
            Assert.Equal(new Rect(-15, 0, ((Control)expander.Content!).Bounds.Width + 30, 32),
                BoundsIn(fill, expander));
            Assert.Equal(new CornerRadius(0), fill.CornerRadius);
            Assert.Equal(fill.Bounds.Size, header.Bounds.Size);
            Assert.InRange(glyph.Center.Y, title.Top, title.Bottom);
            output.WriteLine($"CHEVRON {group.Name}: size={chevron.Bounds.Size}; stroke={chevron.StrokeThickness}");

            Assert.Equal(group.Name, AutomationProperties.GetName(header));
            // WP2 replaces the inert WP1 L6 contract with accessible disclosure headers.
            Assert.True(header.Focusable);
            Assert.True(header.IsTabStop);
            Assert.True(header.IsHitTestVisible);
        }
    }

    private void MeasureEnabled(StackPanel global, string mode)
    {
        var controls = global.GetVisualDescendants().OfType<Control>()
            .Where(c => c is CompactSlider or Button or ComboBox or ListBox ||
                c is ListBoxItem && c.Name?.EndsWith("ChannelButton") == true)
            .Where(c => c.Name != "ChannelPicker")
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
            "Header[Profile]", "Header[White Balance]", "WhiteBalanceModeBox", "WhiteBalanceAutoButton", "WhiteBalancePickerButton",
            "SliderRoot[Temperature]", "SliderRoot[Tint]", "Header[Adjustments]", "SliderRoot[Exposure]", "BrightnessSlider[Brightness]",
            "SliderRoot[Contrast]", "SliderRoot[Highlights]", "SliderRoot[Shadows]", "SliderRoot[Whites]", "SliderRoot[Blacks]",
            "Header[Presence]", "TextureSlider[Texture]", "ClaritySlider[Clarity]", "VibranceSlider[Vibrance]",
            "SaturationSlider[Saturation]", "Header[Tone Curve]", "CompositeChannelButton", "RedChannelButton",
            "GreenChannelButton", "BlueChannelButton", "Reset curve", "Header[Color Mixer]", "RedMixerButton", "OrangeMixerButton",
            "YellowMixerButton", "GreenMixerButton", "AquaMixerButton", "BlueMixerButton", "PurpleMixerButton",
            "MagentaMixerButton", "MixerHueSlider[Hue]", "MixerSaturationSlider[Saturation]",
            "MixerLuminanceSlider[Luminance]", "Header[Detail]", "CaptureSharpenSlider[Sharpen]", "LuminanceNrSlider[Luma NR]",
            "ChromaNrSlider[Chroma NR]", "Header[Effects]", "VignetteSlider[Vignette]", "GrainSlider[Grain]", "ListBoxItem[Medium]",
            "Header[Geometry]", "GeometryVerticalSlider[Vertical]", "GeometryHorizontalSlider[Horizontal]",
            "GeometryAspectSlider[Aspect]", "GeometryDistortionSlider[Distortion]", "Header[Optics]"
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
        ToggleButton { Name: "ExpanderHeader" } header => $"Header[{((DevelopGroup)header.TemplatedParent!).Header}]",
        CompactSlider slider => $"{slider.Name ?? slider.Label}[{slider.Label}]",
        _ => control.Name ?? AutomationProperties.GetName(control) ??
            (control is ContentControl content ? $"{control.GetType().Name}[{content.Content}]" : control.GetType().Name)
    };

    internal static void AssertDisclosureTriangle(Avalonia.Controls.Shapes.Path triangle, bool expanded)
    {
        var muted = ThemeResourceTests.Brush("TextMuted", triangle.ActualThemeVariant).Color;

        Assert.Equal(Avalonia.Media.Geometry.Parse("M3,4.25 L9,4.25 L6,7.75 Z").Bounds, triangle.Data!.Bounds);
        Assert.Equal(new Size(12, 12), triangle.Bounds.Size);
        Assert.Equal(Avalonia.Media.Stretch.None, triangle.Stretch);
        Assert.Equal(muted, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(triangle.Fill).Color);
        Assert.Equal(0.6, triangle.Opacity);
        Assert.Equal(expanded ? 0 : 90, Assert.IsType<Avalonia.Media.RotateTransform>(triangle.RenderTransform).Angle);
    }

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



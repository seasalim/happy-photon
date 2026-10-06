using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopGroupBlockTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlocksSpanThePaneAndCollapsedNeighboursAreFlush(bool gray)
    {
        await DevelopCollapseBaselineTests.WithWindow((vm, window, groups, scroll) =>
        {
            using var theme = new TestUiScope(theme: gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
            scroll.Offset = default;
            DevelopCollapseBaselineTests.Settle(window);
            ShowcaseTestHelper.SettleExpanderChevrons(window);
            var tools = window.GetVisualDescendants().OfType<StackPanel>()
                .Single(control => control.Name == "DevelopToolRow");
            Assert.Equal(8, Bounds(DevelopCollapseBaselineTests.Header(groups[0]), window).Top - Bounds(tools, window).Bottom);

            foreach (var group in groups)
            {
                var header = DevelopCollapseBaselineTests.Header(group);
                Assert.Equal(32, header.Bounds.Height);
                Assert.Equal(950, Bounds(header, window).Left);
                Assert.Equal(1200, Bounds(header, window).Right);
                AssertBrush(header.Background, "SurfaceHigh", header);
                var fill = header.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ToggleButtonBackground");
                AssertBrush(fill.Background, "SurfaceHigh", header);
                var mark = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                    .Single(path => path.Name == "ExpandCollapseChevron");
                Assert.Equal(1182, Bounds(mark, window).Right);
                Assert.Equal(Bounds(header, window).Center.Y, Bounds(mark, window).Center.Y);
                var content = (Control)group.Content!;
                Assert.Equal(8, Bounds(content, window).Top - Bounds(header, window).Bottom);
                Assert.Equal(4, Bounds(group, window).Bottom - Bounds(content, window).Bottom);
                Assert.DoesNotContain(group.GetVisualDescendants(), visual => visual.Name == "GroupDivider");
                group.IsExpanded = false;
            }

            DevelopCollapseBaselineTests.Settle(window);

            for (var i = 1; i < groups.Length; i++)
            {
                Assert.Equal(Bounds(DevelopCollapseBaselineTests.Header(groups[i - 1]), window).Bottom,
                    Bounds(DevelopCollapseBaselineTests.Header(groups[i]), window).Top);
            }

            var presets = window.GetVisualDescendants().OfType<PresetsPanel>().Single();

            var presetHeaders = presets.GetVisualDescendants().OfType<ToggleButton>().Where(c => c.Name == "ExpanderHeader").ToArray();
            Assert.Equal(6, presetHeaders.Length);

            foreach (var header in presetHeaders)
            {
                Assert.Equal(32, header.Bounds.Height);
                AssertBrush(header.Background, "SurfaceMid", header);
                var mark = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>()
                    .Single(path => path.Name == "ExpandCollapseChevron");
                Assert.Equal(18, presets.Bounds.Width - Bounds(mark, presets).Right);
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task LockedHeadersUseOneDisabledOpacityAndMutedTitles()
    {
        await DevelopCollapseBaselineTests.WithWindow(async (vm, window, groups, scroll) =>
        {
            await vm.ToggleCropModeCommand.ExecuteAsync(null);
            DevelopCollapseBaselineTests.Settle(window);
            Assert.True(window.TryFindResource("DisabledOpacity", window.ActualThemeVariant, out var disabled));

            foreach (var group in groups)
            {
                var header = DevelopCollapseBaselineTests.Header(group);
                var fill = header.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ToggleButtonBackground");
                var effective = fill.GetVisualAncestors().Prepend(fill).Aggregate(1d, (value, visual) => value * visual.Opacity);
                Assert.Equal(Assert.IsType<double>(disabled), effective);
                var title = header.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("section-label"));
                AssertBrush(title.Foreground, "TextMuted", title);
                AssertBrush(fill.Background, "SurfaceHigh", header);
            }
        });
    }

    [AvaloniaFact]
    public async Task BottomBandsAlignWithTheUnchangedViewerReference()
    {
        await DevelopCollapseBaselineTests.WithWindow((vm, window, groups, scroll) =>
        {
            var viewer = Named<Border>(window, "DevelopControlBar");
            Assert.Equal(new Rect(241, 643, 708, 33), Bounds(viewer, window));
            var fit = Named<Button>(window, "ZoomFitButton");
            Assert.Equal(660, Bounds(fit, window).Center.Y);
            var reset = Named<Button>(window, "ResetAdjustmentsButton");
            var clear = Named<Button>(window, "ClearHistoryButton");
            Assert.Equal(1190, Bounds(reset, window).Right);

            foreach (var button in new[] { fit, reset, clear })
            {
                Assert.Equal(660, Bounds(button, window).Center.Y);
                AssertBand(button.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("pane-bottom-band")));
            }

            var history = Named<EditHistoryPanel>(window, "EditHistoryPanel");
            var list = history.FindControl<ScrollViewer>("HistoryScrollViewer")!;
            var headerBand = clear.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("pane-bottom-band"));
            // Model the populated list's layout, then its collapse, without creating edits.
            list.Height = 48;
            DevelopCollapseBaselineTests.Settle(window);
            Assert.Equal(new Thickness(0), headerBand.BorderThickness);
            Assert.Equal(32, headerBand.Bounds.Height);
            list.IsVisible = false;
            list.Height = 0;
            DevelopCollapseBaselineTests.Settle(window);
            AssertBand(headerBand);

            vm.IsDevelopMode = false;
            DevelopCollapseBaselineTests.Settle(window);
            var sync = Named<Button>(window, "SyncSettingsButton");
            var previous = Named<BrowseGridFooter>(window, "BrowseGridFooter").FindControl<Button>("PreviousImageButton")!;
            Assert.Equal(1190, Bounds(sync, window).Right);

            foreach (var button in new[] { sync, previous })
            {
                Assert.Equal(660, Bounds(button, window).Center.Y);
                // Browse keeps the band's geometry but hides the hairline, matching its bar-less left pane.
                AssertBand(button.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("pane-bottom-band")), "SurfaceLow");
            }

            return Task.CompletedTask;
        });
    }

    private static void AssertBand(Border band, string hairline = "Divider")
    {
        Assert.Equal(33, band.Bounds.Height);
        Assert.Equal(new Thickness(10, 4), band.Padding);
        Assert.Equal(new Thickness(0, 1, 0, 0), band.BorderThickness);
        AssertBrush(band.Background, "SurfaceLow", band);
        AssertBrush(band.BorderBrush, hairline, band);
    }

    private static void AssertBrush(IBrush? brush, string token, Control control) =>
        Assert.Equal(ThemeResourceTests.Brush(token, control.ActualThemeVariant).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color);

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static Rect Bounds(Control control, Visual root) =>
        new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);
}

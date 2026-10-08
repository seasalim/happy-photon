using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

// FIXES-DEVELOP-WP12 G3: the scope plot's height follows its width, in Develop and Browse.
public sealed class DevelopScopeHeightTests
{
    [AvaloniaTheory]
    [InlineData(250, 80)]
    [InlineData(350, 116)]
    [InlineData(450, 152)]
    public async Task ScopeHeightFollowsTheRightColumn(int column, double expected)
    {
        await DevelopToolsBaselineTests.WithScene("normal", 1200, 700, async (vm, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns the MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            var shell = (Grid)window.FindControl<Border>("WorkspaceLeftPanel")!.Parent!;
            shell.ColumnDefinitions[4].Width = new GridLength(column);
            ShellPaneLimitsTests.Settle(window);
            var panel = window.FindControl<DevelopEditPanel>("DevelopEditPanel")!;
            var histogram = panel.FindControl<HistogramView>("DevelopHistogram")!;
            var band = panel.FindControl<Border>("DevelopScopeBox")!;
            var toolRow = panel.FindControl<StackPanel>("DevelopToolRow")!;
            var scroll = panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!;
            scroll.Offset = default;
            ShellPaneLimitsTests.Settle(window);
            var profile = panel.GetVisualDescendants().OfType<DevelopGroup>().First();

            Assert.Equal(column, shell.ColumnDefinitions[4].ActualWidth);
            AssertPlot(histogram, column, expected, indicators: true);
            Assert.Equal(Box(band, panel).Bottom + 8, Box(toolRow, panel).Y);
            Assert.Equal(Box(toolRow, panel).Bottom + 8, Box(profile, panel).Y);

            var bandHeight = band.Bounds.Height;
            var waveformButton = panel.FindControl<ScopeSelectorRow>("ScopeSelector")!
                .FindControl<ToggleButton>("WaveformScopeButton")!;
            waveformButton.Command!.Execute(waveformButton.CommandParameter);
            ShellPaneLimitsTests.Settle(window);
            var waveform = panel.FindControl<WaveformView>("DevelopWaveform")!;
            var image = waveform.FindControl<Image>("WaveformImage")!;

            Assert.True(waveform.IsVisible);
            Assert.Equal(new Size(column - 28, expected), image.Bounds.Size);
            Assert.Equal(bandHeight, band.Bounds.Height);

            vm.IsDevelopMode = false;
            ShellPaneLimitsTests.Settle(window);
            var review = window.FindControl<BrowseReviewPane>("BrowseReviewPane")!;
            var browseBand = review.FindControl<Border>("BrowseHistogramBox")!;
            var content = (Control)review.GetVisualDescendants().OfType<ScrollViewer>().First().Content!;

            AssertPlot(review.FindControl<HistogramView>("BrowseHistogram")!, column, expected, indicators: false);
            Assert.Equal(Box(browseBand, review).Bottom + 8, Box(content, review).Y);
            await Task.CompletedTask;
        });
    }

    private static void AssertPlot(HistogramView histogram, int column, double expected, bool indicators)
    {
        var canvas = histogram.FindControl<Canvas>("HistogramCanvas")!;
        var floor = histogram.FindControl<Control>("DisplayFloorTriangleTarget")!;
        var highlight = histogram.FindControl<Control>("SceneHighlightTriangleTarget")!;

        Assert.Equal(new Size(column - 28, expected), canvas.Bounds.Size);
        Assert.Equal(indicators, floor.IsEffectivelyVisible);

        if (!indicators) return;

        Assert.Equal(new Point(0, 0), floor.TranslatePoint(default, canvas));
        Assert.Equal(
            new Point(canvas.Bounds.Width, 0),
            highlight.TranslatePoint(new Point(highlight.Bounds.Width, 0), canvas));
    }

    private static Rect Box(Control control, Visual host) =>
        new(control.TranslatePoint(default, host)!.Value, control.Bounds.Size);
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace HappyPhoton.Tests;

public sealed class ExportExpanderHeaderTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HeadersUseSharedMetricsAndCompleteChevrons(bool expanded, bool wrapping)
    {
        using var fixture = new CatalogVmFixture("export-headers");
        using var catalog = fixture.CreateCatalog();
        await using var vm = fixture.CreateViewModel(catalog);
        vm.WorkspaceMode = WorkspaceMode.Export;
        vm.ExportSettings.Watermark.Enabled = expanded;
        vm.ExportSettings.Watermark.Text = wrapping ? "A long watermark title that must wrap inside the expanded pane" : "Jane Doe";
        vm.ExportSettings.OutputColorSpace = OutputColorSpace.DisplayP3;
        vm.ExportSettings.OutputSharpening = OutputSharpeningMode.Print;
        var pane = new ExportSettingsPane { DataContext = vm };
        using var scope = new TestUiScope(new Window { Width = 250, Height = 1400, Content = pane });
        var expanders = new[] { pane.FindControl<Expander>("ExportMoreOptions")!,
            pane.FindControl<Expander>("ExportWatermarkExpander")! };

        foreach (var expander in expanders) expander.IsExpanded = expanded;

        Dispatcher.UIThread.RunJobs();
        ShowcaseTestHelper.SettleExpanderChevrons(pane);

        ToggleButton? firstHeader = null;

        foreach (var expander in expanders)
        {
            var header = expander.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => button.Name == "ExpanderHeader");
            firstHeader ??= header;
            Assert.Same(firstHeader.Theme, header.Theme);
            Assert.Same(firstHeader.Template, header.Template);
            Assert.Equal(new Thickness(10, 0), header.Padding);
            var chevron = expander.GetVisualDescendants().OfType<ShapePath>()
                .Single(path => path.Name == "ExpandCollapseChevron");
            var border = Assert.IsType<Border>(chevron.GetVisualParent());
            Assert.Equal(new Size(16, 16), border.Bounds.Size);
            DevelopHeaderBaselineTests.AssertDisclosureTriangle(chevron, expanded);
            Assert.True(new Rect(border.Bounds.Size).Contains(chevron.Bounds));
            Assert.Equal(32, header.Bounds.Height);
            Assert.Equal(0, header.TranslatePoint(default, pane)!.Value.X);
            Assert.Equal(pane.Bounds.Width, header.Bounds.Width);
            Assert.Equal(18, pane.Bounds.Width - chevron.TranslatePoint(new Point(12, 0), pane)!.Value.X, 6);
            var title = expander.GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text is "More options" or "Watermark");
            Assert.Equal(10, title.TranslatePoint(default, header)!.Value.X);
            Assert.Equal(16, title.TranslatePoint(new Point(0, title.Bounds.Height / 2), header)!.Value.Y);
            Assert.Equal(title.Text, AutomationProperties.GetName(header));
            Assert.Null(ToolTip.GetTip(header));
            var fill = header.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ToggleButtonBackground");
            Assert.Equal(ThemeResourceTests.Brush("SurfaceMid", pane.ActualThemeVariant).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(fill.Background).Color);
            var content = Assert.IsType<StackPanel>(expander.Content);
            var summary = Assert.IsType<TextBlock>(content.Children[0]);
            Assert.Equal(expander == expanders[0] ? vm.ExportOptionsSummary : vm.WatermarkSummary, summary.Text);
            var presenter = expander.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                .Single(control => control.Name == "PART_ContentPresenter" && ReferenceEquals(control.TemplatedParent, expander));
            Assert.Equal(expanded, presenter.IsVisible);

            if (expanded)
            {
                Assert.Equal(15, content.TranslatePoint(default, pane)!.Value.X);
                Assert.Equal(8, summary.TranslatePoint(default, header)!.Value.Y - header.Bounds.Height);
                Assert.Equal(0, summary.Bounds.Top);

                if (wrapping && expander == expanders[1]) Assert.True(summary.TextLayout.TextLines.Count > 1);
            }
        }

        if (!expanded)
        {
            Assert.Equal(expanders[0].Bounds.Bottom, expanders[1].Bounds.Top);
            var off = expanders[1].GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Off");
            Assert.True(off.IsEffectivelyVisible);
        }
    }
}

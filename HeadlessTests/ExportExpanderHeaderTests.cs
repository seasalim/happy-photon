using Avalonia;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeadersUseSharedMetricsAndCompleteChevrons(bool expanded)
    {
        using var fixture = new CatalogVmFixture("export-headers");
        using var catalog = fixture.CreateCatalog();
        await using var vm = fixture.CreateViewModel(catalog);
        vm.WorkspaceMode = WorkspaceMode.Export;
        vm.ExportSettings.Watermark.Enabled = expanded;
        vm.ExportSettings.Watermark.Text = "Jane Doe";
        var pane = new ExportSettingsPane { DataContext = vm };
        using var scope = new TestUiScope(new Window { Width = 280, Height = 1100, Content = pane });
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
            Assert.Equal(new Thickness(8, 4), header.Padding);
            var chevron = expander.GetVisualDescendants().OfType<ShapePath>()
                .Single(path => path.Name == "ExpandCollapseChevron");
            var border = Assert.IsType<Border>(chevron.GetVisualParent());
            Assert.Equal(new Size(32, 32), border.Bounds.Size);
            Assert.Equal(new Size(14, 7), chevron.Data!.Bounds.Size);
            Assert.Equal(chevron.Data.Bounds.Size, chevron.Bounds.Size);
            Assert.True(new Rect(border.Bounds.Size).Contains(chevron.Bounds));
            Assert.Equal(expanded ? 180 : 0, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);
            var title = expander.GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text is "More options" or "Watermark");
            Assert.Equal(8, title.TranslatePoint(default, header)!.Value.X);
        }
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class VisualsTypeGateTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public void StockControls_RenderAtBodySize(ThemeVariant theme)
    {
        var button = new Button { Content = "Button probe" };
        var toggle = new ToggleButton { Content = "ToggleButton probe" };
        var combo = new ComboBox { ItemsSource = new[] { "ComboBox probe" }, SelectedIndex = 0 };
        var entry = new TextBox { Text = "TextBox probe" };
        var check = new CheckBox { Content = "CheckBox probe" };
        var menuItem = new MenuItem { Header = "MenuItem probe" };
        var text = new TextBlock { Text = "TextBlock probe" };
        var menu = new Menu { Items = { menuItem } };
        var panel = new StackPanel
        {
            Children = { button, toggle, combo, entry, check, menu, text }
        };
        var window = new Window { Content = panel, Width = 600, Height = 500 };
        ToolTip.SetTip(button, "ToolTip probe");
        using var scope = new TestUiScope(window, theme);
        Dispatcher.UIThread.RunJobs();

        ObserveText(theme, "Button", button, "Button probe");
        ObserveText(theme, "ToggleButton", toggle, "ToggleButton probe");
        ObserveText(theme, "ComboBox", combo, "ComboBox probe");
        var presenter = entry.GetVisualDescendants().OfType<TextPresenter>().Single();
        Observe(theme, "TextBox", presenter.FontSize, presenter.Bounds.Width, presenter.Bounds.Height);
        ObserveText(theme, "CheckBox", check, "CheckBox probe");
        ObserveText(theme, "MenuItem", menuItem, "MenuItem probe");
        Observe(theme, "TextBlock", text.FontSize, text.Bounds.Width, text.Bounds.Height);

        try
        {
            ToolTip.SetIsOpen(button, true);
            Dispatcher.UIThread.RunJobs();
            var tip = window.GetVisualDescendants().OfType<ToolTip>().Single();
            ShowcaseTestHelper.Settle(() => tip.Opacity == 1, "Type gate tooltip fade-in");
            ObserveText(theme, "ToolTip", tip, "ToolTip probe");
        }
        finally
        {
            ToolTip.SetIsOpen(button, false);
        }
    }

    private void ObserveText(ThemeVariant theme, string name, Control control, string content)
    {
        var text = control.GetVisualDescendants().OfType<TextBlock>()
            .Single(candidate => candidate.Text == content);
        Observe(theme, name, text.FontSize, text.Bounds.Width, text.Bounds.Height);
    }

    private void Observe(ThemeVariant theme, string name, double size, double width, double height)
    {
        Assert.Equal(11, size);
        Assert.True(width > 0 && height > 0, $"{name} has not been laid out.");
        output.WriteLine($"G1 theme={theme.Key} control={name} renderedFontSize={size} bounds={width}x{height}");
    }
}

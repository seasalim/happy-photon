using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ToolTipThemeTests
{
    [AvaloniaTheory]
    [MemberData(nameof(ThemeResourceTests.Variants), MemberType = typeof(ThemeResourceTests))]
    public void OrdinaryTooltipIsOpaqueAndWrapsLongTextInBothThemes(ThemeVariant theme)
    {
        var button = new Button { Content = "Tooltip target" };
        ToolTip.SetTip(button, string.Join(" ", Enumerable.Repeat("A longer description that must wrap.", 8)));
        var window = new Window { Content = button, Width = 600, Height = 400 };
        using var scope = new TestUiScope(window, theme);

        ToolTip.SetIsOpen(button, true);
        Dispatcher.UIThread.RunJobs();
        var tip = window.GetVisualDescendants().OfType<ToolTip>().Single();
        ShowcaseTestHelper.Settle(() => tip.Opacity == 1, "Tooltip fade-in");

        var background = Assert.IsAssignableFrom<ISolidColorBrush>(tip.Background);
        var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(tip.Foreground);
        Assert.Equal(255, background.Color.A);
        Assert.Equal(1, background.Opacity);
        Assert.Equal(255, foreground.Color.A);
        Assert.True(ThemeResourceTests.Contrast(background.Color, foreground.Color) >= 4.5);
        Assert.InRange(tip.Bounds.Width, 1, 400);
        var text = tip.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        Assert.True(text.Bounds.Height > text.FontSize * 2);
    }
}

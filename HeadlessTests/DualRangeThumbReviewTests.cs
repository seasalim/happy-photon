using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;
using ThumbMark = Avalonia.Controls.Shapes.Path;

namespace HappyPhoton.Tests;

public sealed class DualRangeThumbReviewTests
{
    [AvaloniaTheory]
    [InlineData(30, 70, 0, "40", 30, -20, 0, 40, 70)]
    [InlineData(30, 70, 1, "60", 70, 20, 1, 30, 60)]
    [InlineData(30, 70, 0, "10", 42, 20, 1, 10, 70)]
    [InlineData(30, 70, 1, "90", 58, -20, 0, 30, 90)]
    [InlineData(50, 50, 0, "40", 50, -20, 1, 40, 50)]
    [InlineData(50, 50, 1, "60", 50, 20, 0, 50, 60)]
    [InlineData(30, 70, 0, "70", 70, -20, 0, 70, 70)]
    public void PendingEntryCommitsBeforeEndpointSelectionAndDragOrigin(
        double lower, double upper, int entryIndex, string draft, double pressValue,
        double delta, int endpoint, double committedLower, double committedUpper)
    {
        var range = new DualRangeTrack { Width = 200, Lower = lower, Upper = upper };
        var window = new Window { Width = 200, Height = 100, Content = range };
        using var scope = new TestUiScope(window);
        var canvas = range.GetVisualDescendants().OfType<Canvas>().Single();
        var entries = range.GetVisualDescendants().OfType<TextBox>().ToArray();
        var marks = canvas.Children.OfType<ThumbMark>().ToArray();
        Assert.True(entries[entryIndex].Focus());
        entries[entryIndex].SelectAll();
        window.KeyTextInput(draft);
        var start = canvas.TranslatePoint(new Point(6 + pressValue / 100 * 188, 12), window)!.Value;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(delta, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(delta, 0), MouseButton.Left);

        var expectedLower = endpoint == 0
            ? Math.Clamp(committedLower + delta / 188 * 100, 0, committedUpper) : committedLower;
        var expectedUpper = endpoint == 1
            ? Math.Clamp(committedUpper + delta / 188 * 100, committedLower, 100) : committedUpper;
        Assert.Equal(expectedLower, range.Lower, 8);
        Assert.Equal(expectedUpper, range.Upper, 8);
        Assert.True(marks[endpoint].IsFocused);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CoincidentLowerActiveMarkPaintsAboveUpper(bool gray, bool drag)
    {
        var range = new DualRangeTrack { Width = 200, Lower = 50, Upper = 50 };
        var window = new Window { Width = 200, Height = 100, Content = range };
        using var scope = new TestUiScope(window, gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
        var canvas = range.GetVisualDescendants().OfType<Canvas>().Single();
        var marks = canvas.Children.OfType<ThumbMark>().ToArray();
        var start = canvas.TranslatePoint(new Point(100, 12), window)!.Value;

        if (drag)
        {
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start + new Vector(-20, 0), RawInputModifiers.LeftMouseButton);
            window.MouseMove(start + new Vector(20, 0), RawInputModifiers.LeftMouseButton);
            Assert.Contains("pointer-captured", marks[0].Classes);
        }
        else
        {
            Assert.True(marks[0].Focus(NavigationMethod.Tab));
        }

        Assert.Equal(50, range.Lower);
        Assert.Equal(50, range.Upper);
        AssertActive(marks[0]);
        Assert.True(marks[0].ZIndex > marks[1].ZIndex,
            $"Lower ZIndex={marks[0].ZIndex}; Upper ZIndex={marks[1].ZIndex}");

        if (drag)
        {
            window.MouseUp(start + new Vector(20, 0), MouseButton.Left);
        }

        Assert.True(marks[1].Focus(NavigationMethod.Tab));
        AssertActive(marks[1]);
        Assert.True(marks[1].ZIndex > marks[0].ZIndex);
        window.KeyPress(Key.Tab, RawInputModifiers.Shift, PhysicalKey.None, null);
        Assert.True(marks[0].IsFocused);
        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.None, null);
        Assert.True(marks[1].IsFocused);
    }

    [AvaloniaTheory]
    [InlineData(false, 0, Key.Enter)]
    [InlineData(false, 0, Key.Escape)]
    [InlineData(false, 1, Key.Enter)]
    [InlineData(false, 1, Key.Escape)]
    [InlineData(true, 0, Key.Enter)]
    [InlineData(true, 0, Key.Escape)]
    [InlineData(true, 1, Key.Enter)]
    [InlineData(true, 1, Key.Escape)]
    public void FinishingEntryRestoresKeyboardFocusColor(bool gray, int endpoint, Key key)
    {
        var range = new DualRangeTrack { Width = 200, Lower = 30, Upper = 70 };
        var window = new Window { Width = 200, Height = 100, Content = range };
        using var scope = new TestUiScope(window, gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
        var entries = range.GetVisualDescendants().OfType<TextBox>().ToArray();
        var marks = range.GetVisualDescendants().OfType<ThumbMark>().ToArray();
        Assert.True(entries[endpoint].Focus(NavigationMethod.Tab));
        entries[endpoint].SelectAll();
        window.KeyTextInput("50");
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);

        Assert.True(marks[endpoint].IsFocused);
        Assert.Equal(key == Key.Enter && endpoint == 0 ? 50 : 30, range.Lower);
        Assert.Equal(key == Key.Enter && endpoint == 1 ? 50 : 70, range.Upper);
        AssertActive(marks[endpoint]);
    }

    private static void AssertActive(ThumbMark mark)
    {
        Dispatcher.UIThread.RunJobs();
        var expected = Assert.IsAssignableFrom<ISolidColorBrush>(
            mark.FindResource(mark.ActualThemeVariant, "ControlActive")).Color;
        Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(mark.Fill).Color);
    }
}

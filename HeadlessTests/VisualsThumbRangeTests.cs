using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;
using ThumbMark = Avalonia.Controls.Shapes.Path;

namespace HappyPhoton.Tests;

public sealed class VisualsThumbRangeTests
{
    [AvaloniaTheory]
    [InlineData(30, 70, 54.4, -20, 0)]
    [InlineData(30, 70, 145.6, 20, 1)]
    [InlineData(50, 50, 100, -20, 0)]
    [InlineData(50, 50, 100, 20, 1)]
    public void PressPreservesValueAndDragSelectsTheEndpoint(
        double lower, double upper, double pressX, double delta, int endpoint)
    {
        var range = new DualRangeTrack { Width = 200, Lower = lower, Upper = upper };
        var window = new Window { Width = 200, Height = 100, Content = range };
        using var scope = new TestUiScope(window);
        var canvas = range.GetVisualDescendants().OfType<Canvas>().Single();
        var marks = canvas.Children.OfType<ThumbMark>().ToArray();
        var start = canvas.TranslatePoint(new Point(pressX, 12), window)!.Value;
        var starts = 0;
        var ends = 0;
        range.AddHandler(CompactSlider.DragStartedEvent, (_, _) => starts++);
        range.AddHandler(CompactSlider.DragCompletedEvent, (_, _) => ends++);

        window.MouseDown(start, MouseButton.Left);
        Assert.Equal(lower, range.Lower);
        Assert.Equal(upper, range.Upper);

        if (lower == upper)
        {
            window.MouseMove(start + new Vector(-Math.Sign(delta), 0), RawInputModifiers.LeftMouseButton);
            Assert.Equal(lower, range.Lower);
            Assert.Equal(upper, range.Upper);
            Assert.All(marks, mark => Assert.DoesNotContain("pointer-captured", mark.Classes));
        }

        window.MouseMove(start + new Vector(delta, 0), RawInputModifiers.LeftMouseButton);
        Assert.True(marks[endpoint].IsFocused);
        Assert.Contains("pointer-captured", marks[endpoint].Classes);
        window.MouseUp(start + new Vector(delta, 0), MouseButton.Left);
        Assert.Equal(lower + (endpoint == 0 ? delta / 188 * 100 : 0), range.Lower, 8);
        Assert.Equal(upper + (endpoint == 1 ? delta / 188 * 100 : 0), range.Upper, 8);
        Assert.Equal(1, starts);
        Assert.Equal(1, ends);
        Assert.All(marks, mark => Assert.DoesNotContain("pointer-captured", mark.Classes));
    }
}

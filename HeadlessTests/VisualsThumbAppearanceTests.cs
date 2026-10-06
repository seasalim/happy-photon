using Avalonia;
using Avalonia.Automation.Peers;
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

public sealed class VisualsThumbAppearanceTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MarksUseThemeStatesAndKeepRangeAutomation(bool gray)
    {
        var slider = new CompactSlider { Width = 200, Label = "Exposure" };
        var range = new DualRangeTrack { Width = 200, Lower = 30, Upper = 70 };
        var panel = new StackPanel { Margin = new Thickness(10), Spacing = 10 };
        panel.Children.Add(slider);
        panel.Children.Add(range);
        var window = new Window { Width = 300, Height = 180, Content = panel };
        using var scope = new TestUiScope(window, gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
        var mark = slider.FindControl<ThumbMark>("ThumbMark")!;
        var marks = range.GetVisualDescendants().OfType<ThumbMark>().ToArray();
        var outside = new Point(290, 170);
        window.MouseMove(outside);
        AssertColor(mark, "TextMuted");
        Assert.All(marks, thumb => AssertColor(thumb, "TextMuted"));
        Assert.False(mark.IsHitTestVisible);
        Assert.All(marks, thumb => Assert.False(thumb.IsHitTestVisible));
        Assert.Same(marks[0].Data, marks[1].Data);
        Assert.Equal(new Rect(0, 0, 9, 7), mark.Data!.Bounds);
        Assert.Same(mark.FindResource("SliderThumbOutline"), mark.Stroke);
        Assert.All(marks, thumb => Assert.Null(thumb.Stroke));

        window.MouseMove(slider.TranslatePoint(new Point(5, 10), window)!.Value);
        AssertColor(mark, "TextSecondary");
        Assert.True(slider.Focus(NavigationMethod.Tab));
        AssertColor(mark, "ControlActive");
        window.MouseMove(outside);
        AssertColor(mark, "ControlActive");
        window.FocusManager!.Focus(null);
        AssertColor(mark, "TextMuted");

        var sliderPoint = slider.FindControl<Grid>("TrackGrid")!
            .TranslatePoint(new Point(10, 10), window)!.Value;
        window.MouseDown(sliderPoint, MouseButton.Left);
        AssertColor(mark, "ControlActive");
        window.MouseUp(sliderPoint, MouseButton.Left);
        AssertColor(mark, "TextSecondary");

        var canvas = range.GetVisualDescendants().OfType<Canvas>().Single();
        var rangePoint = canvas.TranslatePoint(new Point(62, 12), window)!.Value;
        window.MouseMove(rangePoint);
        Assert.All(marks, thumb => AssertColor(thumb, "TextSecondary"));

        for (var i = 0; i < marks.Length; i++)
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(marks[i]);
            Assert.NotNull(peer);
            Assert.Equal(i == 0 ? "Luminance lower limit" : "Luminance upper limit", peer.GetName());
            Assert.True(peer.IsKeyboardFocusable());
            Assert.True(peer.IsControlElement());
            Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
            Assert.True(marks[i].Focus(NavigationMethod.Tab));
            AssertColor(marks[i], "ControlActive");
            AssertColor(marks[1 - i], "TextSecondary");
        }

        window.MouseDown(rangePoint, MouseButton.Left);
        AssertColor(marks[0], "ControlActive");
        AssertColor(marks[1], "TextSecondary");
        window.MouseUp(rangePoint, MouseButton.Left);
        window.FocusManager.Focus(null);
        window.MouseMove(outside);
        Assert.All(marks, thumb => AssertColor(thumb, "TextMuted"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemperatureMarkSitsOnGradientTrackAndCenterMarkIsAboveTrack(bool gray)
    {
        var panel = new DevelopEditPanel();
        var window = new Window { Width = 200, Height = 2400, Content = panel };
        using var scope = new TestUiScope(window, gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
        var global = panel.GetVisualDescendants().OfType<StackPanel>()
            .Single(control => control.Classes.Contains("global-edits"));
        var slider = global.GetVisualDescendants().OfType<CompactSlider>()
            .Single(control => control.Label == "Temperature");
        slider.Minimum = -100;
        slider.Maximum = 100;
        slider.Value = 0;
        window.UpdateLayout();
        var mark = slider.FindControl<ThumbMark>("ThumbMark")!;
        var track = slider.FindControl<Border>("TrackLine")!;
        Assert.IsType<LinearGradientBrush>(slider.TrackBrush);
        var point = track.TranslatePoint(new Point(track.Bounds.Width / 2, 1), window)!.Value;
        var visible = Pixel(window, point);
        mark.IsVisible = false;
        var hidden = Pixel(window, point);
        Assert.NotEqual(hidden, visible);
        Assert.NotEqual(Pixel(window, new Point(0, 0)), hidden);
        mark.IsVisible = true;
        AssertColor(mark, "TextSecondary");

        slider.ShowValueFill = true;
        window.UpdateLayout();
        var center = slider.FindControl<Border>("CenterMark")!;
        Assert.True(center.Bounds.Bottom <= track.Bounds.Top);
        Assert.True(mark.Bounds.Top < track.Bounds.Top);
        Assert.True(mark.Bounds.Bottom > track.Bounds.Bottom);
    }

    private static void AssertColor(ThumbMark mark, string resource)
    {
        Dispatcher.UIThread.RunJobs();
        var expected = Assert.IsAssignableFrom<ISolidColorBrush>(mark.FindResource(mark.ActualThemeVariant, resource)).Color;
        Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(mark.Fill).Color);
    }

    private static unsafe uint Pixel(Window window, Point point)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        uint pixel = 0;
        frame.CopyPixels(new PixelRect((int)point.X, (int)point.Y, 1, 1), (nint)(&pixel), 4, 4);

        return pixel;
    }
}

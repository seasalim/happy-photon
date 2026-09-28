using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CompactSliderWheelTests
{
    [AvaloniaTheory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(-2, 0, -2)]
    [InlineData(5, -1, -1)]
    [InlineData(0, .25, .25)]
    [InlineData(.25, 0, .25)]
    public void DeltasAccumulateWholeSmallChanges(double x, double y, double delta)
    {
        var clock = new TestTimeProvider();
        var slider = new CompactSlider { SmallChange = .5, WheelTimeProvider = clock };
        var window = new Window { Width = 250, Height = 50, Content = slider };
        using var scope = new TestUiScope(window);
        var starts = 0;
        var ends = 0;
        slider.DragStarted += (_, _) => starts++;
        slider.DragCompleted += (_, _) => ends++;

        for (var i = 1; i <= 4; i++)
        {
            Assert.True(Wheel(slider, x, y).Handled);
            Assert.Equal(Math.Truncate(i * delta) * .5, slider.Value);
        }

        Assert.Equal(1, starts);
        Assert.Equal(0, ends);

        clock.Advance(TimeSpan.FromMilliseconds(299));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, ends);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, ends);
        Assert.Equal(0, clock.TimerCount);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WheelClampsAndKeyboardAndDragKeepTheirBoundsPolicy(bool wrap)
    {
        var slider = new CompactSlider { Minimum = 0, Maximum = 10, SmallChange = 1,
            Value = 9, WrapValue = wrap, WheelTimeProvider = new TestTimeProvider() };
        var window = new Window { Width = 250, Height = 50, Content = slider };
        using var scope = new TestUiScope(window);

        Wheel(slider, 0, 20);

        Assert.Equal(wrap ? 9 : 10, slider.Value);

        Wheel(slider, 0, 1);

        Assert.Equal(wrap ? 9 : 10, slider.Value);

        Wheel(slider, 0, -30);

        Assert.Equal(0, slider.Value);

        Wheel(slider, 0, -1);

        Assert.Equal(0, slider.Value);

        Assert.True(slider.Focus());

        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);

        Assert.Equal(wrap ? 9 : 0, slider.Value);

        slider.Value = 9;
        var track = slider.FindControl<Grid>("TrackGrid")!;
        var start = track.TranslatePoint(new Point(track.Bounds.Width / 2, 5), window)!.Value;
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(start + new Vector(track.Bounds.Width * .2, 0), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(track.Bounds.Width * .2, 0), MouseButton.Left, RawInputModifiers.None);

        Assert.Equal(wrap ? 1 : 10, slider.Value);
    }

    [AvaloniaTheory]
    [InlineData(.1)]
    [InlineData(-.1)]
    public void DecimalFractionsDoNotLoseAWholeNotchToRounding(double delta)
    {
        var slider = new CompactSlider { WheelTimeProvider = new TestTimeProvider() };
        var window = new Window { Content = slider };
        using var scope = new TestUiScope(window);

        for (var i = 0; i < 9; i++) Wheel(slider, 0, delta);

        Assert.Equal(0, slider.Value);

        Wheel(slider, 0, delta);

        Assert.Equal(Math.Sign(delta), slider.Value);
    }

    [AvaloniaFact]
    public void PlainWheelAndDisabledAncestorsLeaveEventUntouched()
    {
        var slider = new CompactSlider();
        var parent = new StackPanel { Children = { slider } };
        var window = new Window { Content = parent };
        using var scope = new TestUiScope(window);

        Assert.False(Wheel(slider, 0, 1, KeyModifiers.None).Handled);
        Assert.False(Wheel(slider, 0, 0).Handled);

        slider.IsEnabled = false;

        Assert.False(Wheel(slider, 0, 1).Handled);

        slider.IsEnabled = true;
        parent.IsEnabled = false;

        Assert.False(slider.IsEffectivelyEnabled);
        Assert.False(Wheel(slider, 0, 1).Handled);
        Assert.Equal(0, slider.Value);
    }

    [AvaloniaTheory]
    [InlineData("exit")]
    [InlineData("shift")]
    [InlineData("disable")]
    [InlineData("detach")]
    public void EachEndingClosesExactlyOnceAndCancelsQueuedIdle(string ending)
    {
        var clock = new TestTimeProvider();
        var slider = new CompactSlider { WheelTimeProvider = clock };
        var focusTarget = new TextBox();
        var parent = new StackPanel { Children = { slider, focusTarget } };
        var window = new Window { Width = 250, Height = 100, Content = parent };
        using var scope = new TestUiScope(window);
        var starts = 0;
        var ends = 0;
        slider.DragStarted += (_, _) => starts++;
        slider.DragCompleted += (_, _) => ends++;
        focusTarget.Focus();
        focusTarget.AddHandler(InputElement.KeyUpEvent, (_, e) => e.Handled = true);
        window.MouseMove(new Point(100, 10));
        Wheel(slider, 0, 1);
        clock.Advance(TimeSpan.FromMilliseconds(300)); // Queue the callback without draining it.

        switch (ending)
        {
            case "exit": window.MouseMove(new Point(100, 80)); break;
            case "shift": window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.None, null); break;
            case "disable": parent.IsEnabled = false; break;
            case "detach": parent.Children.Remove(slider); break;
        }

        Assert.Equal(1, ends);

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, starts);
        Assert.Equal(1, ends);
        Assert.Equal(0, clock.TimerCount);
    }

    [AvaloniaFact]
    public void RescrollingInvalidatesQueuedTimeoutAndReleaseClearsFraction()
    {
        var clock = new TestTimeProvider();
        var slider = new CompactSlider { WheelTimeProvider = clock };
        var window = new Window { Content = slider };
        using var scope = new TestUiScope(window);
        var ends = 0;
        slider.DragCompleted += (_, _) => ends++;

        Wheel(slider, 0, 1);
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Wheel(slider, 0, 1);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, ends);

        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.RightShift });

        Assert.Equal(1, ends);

        Wheel(slider, 0, .75);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.LeftShift });
        Wheel(slider, 0, .25);

        Assert.Equal(2, slider.Value);

        clock.Advance(TimeSpan.FromMilliseconds(300));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, ends);
    }

    internal static PointerWheelEventArgs Wheel(CompactSlider slider, double x, double y,
        KeyModifiers modifiers = KeyModifiers.Shift)
    {
        var args = new PointerWheelEventArgs(slider, new Pointer(1, PointerType.Mouse, true),
            slider, new Point(100, 10), 0, new PointerPointProperties(), modifiers, new Vector(x, y));
        slider.RaiseEvent(args);

        return args;
    }
}

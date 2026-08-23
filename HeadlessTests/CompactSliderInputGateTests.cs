using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CompactSliderInputGateTests
{
    [AvaloniaFact]
    public void FirstPress_CapturesWhenAnAncestorHandledTheRoutedEvent()
    {
        const int attemptCount = 100;
        var (slider, window) = ShowSlider();
        var layout = slider.FindControl<Grid>("LayoutGrid")!;
        IPointer? pressedPointer = null;
        window.AddHandler(
            InputElement.PointerPressedEvent,
            (_, args) =>
            {
                pressedPointer = args.Pointer;
                args.Handled = true;
            },
            RoutingStrategies.Tunnel,
            handledEventsToo: true);

        try
        {
            var capturedCount = 0;
            for (var attempt = 0; attempt < attemptCount; attempt++)
            {
                pressedPointer = null;
                window.MouseDown(
                    new Point(110, 11),
                    MouseButton.Left,
                    RawInputModifiers.None);
                if (pressedPointer?.Captured == layout)
                {
                    capturedCount++;
                }

                window.MouseUp(
                    new Point(110, 11),
                    MouseButton.Left,
                    RawInputModifiers.None);
            }

            Assert.True(
                capturedCount == attemptCount,
                $"Captured {capturedCount}/{attemptCount} first presses.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CapturedPress_ChangesThumbColorUntilRelease()
    {
        const int attemptCount = 100;
        var (slider, window) = ShowSlider();
        var thumb = slider.FindControl<Border>("ThumbDot")!;
        var inactiveColor = Assert.IsAssignableFrom<ISolidColorBrush>(
            thumb.Background).Color;

        try
        {
            var visibleCount = 0;
            var capturedClassCount = 0;
            var lastCapturedColor = inactiveColor;
            for (var attempt = 0; attempt < attemptCount; attempt++)
            {
                window.MouseDown(
                    new Point(110, 11),
                    MouseButton.Left,
                    RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                if (thumb.Classes.Contains("pointer-captured"))
                {
                    capturedClassCount++;
                }
                var capturedColor = Assert.IsAssignableFrom<ISolidColorBrush>(
                    thumb.Background).Color;
                lastCapturedColor = capturedColor;
                if (capturedColor != inactiveColor)
                {
                    visibleCount++;
                }

                window.MouseUp(
                    new Point(110, 11),
                    MouseButton.Left,
                    RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(
                    inactiveColor,
                    Assert.IsAssignableFrom<ISolidColorBrush>(
                        thumb.Background).Color);
            }

            Assert.True(
                visibleCount == attemptCount,
                $"Showed capture feedback on {visibleCount}/{attemptCount} presses; " +
                $"captured-class={capturedClassCount}/{attemptCount}, " +
                $"inactive={inactiveColor}, captured={lastCapturedColor}.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CoalescedDrag_AppliesReleasePositionOnEveryAttempt()
    {
        const int attemptCount = 100;
        var (slider, window) = ShowSlider();

        try
        {
            var appliedCount = 0;
            for (var attempt = 0; attempt < attemptCount; attempt++)
            {
                slider.Value = 0;
                window.MouseDown(
                    new Point(110, 11),
                    MouseButton.Left,
                    RawInputModifiers.None);
                window.MouseUp(
                    new Point(150, 11),
                    MouseButton.Left,
                    RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();

                if (slider.Value > 0)
                {
                    appliedCount++;
                }
            }

            Assert.True(
                appliedCount == attemptCount,
                $"Applied {appliedCount}/{attemptCount} coalesced drags.");
        }
        finally
        {
            window.Close();
        }
    }

    private static (CompactSlider Slider, Window Window) ShowSlider()
    {
        var slider = new CompactSlider
        {
            Width = 250,
            Minimum = -100,
            Maximum = 100,
            Value = 0
        };
        var window = new Window
        {
            Width = 250,
            Height = 22,
            Content = slider
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (slider, window);
    }
}

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ShowcaseTestHelperTests
{
    [AvaloniaFact]
    public void SettlePumpsAnimationFramesWithoutVisualChanges()
    {
        var window = new Window { Width = 200, Height = 100 };
        using var scope = new TestUiScope(window);
        using var frame = window.CaptureRenderedFrame();
        var frames = 0;
        window.RequestAnimationFrame(NextFrame);

        ShowcaseTestHelper.Settle(() => frames >= 3, "Animation frames");

        void NextFrame(TimeSpan _)
        {
            frames++;

            if (frames < 3)
            {
                window.RequestAnimationFrame(NextFrame);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettleCompletesChevronsOutsideScrollViewport(bool expanded)
    {
        var expander = new Expander { Header = "Header", IsExpanded = expanded };
        var panel = new StackPanel
        {
            Children = { new Border { Height = 1000 }, expander }
        };
        using var scope = new TestUiScope(new Window
        {
            Width = 280, Height = 200, Content = new ScrollViewer { Content = panel }
        });
        Dispatcher.UIThread.RunJobs();

        ShowcaseTestHelper.SettleExpanderChevrons(panel);
    }

    [AvaloniaFact]
    public void SettlePumpsDispatcherTimerWhenNoRenderWorkIsPending()
    {
        var fired = false;
        var timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        timer.Tick += (_, _) => fired = true;
        Dispatcher.UIThread.RunJobs();
        timer.Start();

        try
        {
            ShowcaseTestHelper.Settle(() => fired, "Dispatcher timer");
            Assert.True(fired);
        }
        finally
        {
            timer.Stop();
        }
    }
}

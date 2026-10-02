using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BatchExportProgressTests
{
    [AvaloniaFact]
    public void ExportProgressBarIsDeterminateAndTracksProgress()
    {
        var strip = new ExportQueueStrip();
        Dispatcher.UIThread.RunJobs();
        var bar = strip.FindControl<ProgressBar>("ExportProgressBar")!;
        var label = strip.FindControl<TextBlock>("ExportProgressLabel")!;

        Assert.False(bar.IsIndeterminate);
        Assert.Equal(2, bar.Height);
        // WP8 uses FontSizeSmall (the minimum) and removes tracking.
        Assert.Equal(10, label.FontSize);
        Assert.Equal(0, label.LetterSpacing);
    }
}

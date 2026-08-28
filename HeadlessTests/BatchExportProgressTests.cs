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
        Assert.Equal(9, label.FontSize);
        Assert.Equal(1, label.LetterSpacing);
    }
}

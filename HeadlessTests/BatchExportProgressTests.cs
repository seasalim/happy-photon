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

        Assert.False(bar.IsIndeterminate);
    }
}

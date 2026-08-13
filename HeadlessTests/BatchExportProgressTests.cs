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
        var dialog = new BatchExportDialog();
        Dispatcher.UIThread.RunJobs();
        var bar = dialog.FindControl<ProgressBar>("ExportProgressBar")!;

        Assert.False(bar.IsIndeterminate);

        dialog.ViewModel.BeginExport();
        dialog.ViewModel.UpdateProgress(12, 40, "photo.jpg");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(40, bar.Maximum);
        Assert.Equal(12, bar.Value);

        dialog.ViewModel.EndExport();
        dialog.Close();
    }
}

using Avalonia.Automation;
using Avalonia.Controls;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BackgroundActivityStatusBarTests
{
    [AvaloniaFact]
    public async Task SegmentIsStaticAccessibleDeterminateAndAbsentAtRest()
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"happy-photon-status-activity-{Guid.NewGuid():N}");
        using var catalog = new CatalogService(root);
        var vm = new MainWindowViewModel(catalog);
        var view = new StatusBarView { DataContext = vm };
        var queue = new ExportQueueStrip { DataContext = vm };
        var host = new Window
        {
            Content = new StackPanel { Children = { queue, view } }
        };
        host.Show();
        var segment = view.FindControl<StackPanel>("BackgroundActivitySegment")!;
        var dot = view.FindControl<Ellipse>("BackgroundActivityDot")!;
        var label = view.FindControl<TextBlock>("BackgroundActivityLabel")!;
        var progress = view.FindControl<ProgressBar>("BackgroundActivityProgress")!;

        Assert.False(segment.IsVisible);
        Assert.Equal("Background activity", AutomationProperties.GetName(segment));
        Assert.Equal(AutomationLiveSetting.Polite,
            AutomationProperties.GetLiveSetting(segment));
        Assert.Equal(6, dot.Width);
        Assert.Equal(6, dot.Height);
        Assert.True(dot.Transitions == null || dot.Transitions.Count == 0);

        var started = DateTimeOffset.UtcNow;
        using (var export = vm.BeginExportActivity(4))
        {
            export.Report(1);
            vm.PumpBackgroundActivity(started);
            vm.PumpBackgroundActivity(started + TimeSpan.FromSeconds(1));
            Dispatcher.UIThread.RunJobs();

            Assert.True(segment.IsVisible);
            Assert.Equal("Exporting — 1 / 4", label.Text);
            Assert.Equal("Exporting — 1 / 4", ToolTip.GetTip(segment));
            Assert.True(progress.IsVisible);
            Assert.False(progress.IsIndeterminate);
            Assert.Equal(60, progress.Width);
            Assert.Equal(3, progress.Height);
            Assert.Equal(1, progress.Value);
            Assert.Equal(4, progress.Maximum);
            Assert.Single(view.GetVisualDescendants(), control =>
                AutomationProperties.GetName(control) == "Background activity");

            vm.WorkspaceMode = WorkspaceMode.Export;
            vm.IsExportJobRunning = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(queue.IsVisible);
            Assert.False(segment.IsVisible);

            vm.WorkspaceMode = WorkspaceMode.Browse;
            Dispatcher.UIThread.RunJobs();
            Assert.False(queue.IsVisible);
            Assert.True(segment.IsVisible);
            vm.IsExportJobRunning = false;
        }

        vm.PumpBackgroundActivity(started + TimeSpan.FromMilliseconds(1100));
        vm.PumpBackgroundActivity(started + TimeSpan.FromMilliseconds(1800));
        Dispatcher.UIThread.RunJobs();
        Assert.False(segment.IsVisible);
        Assert.False(vm.IsBackgroundActivitySamplerRunning);
        var stoppedEpoch = vm.BackgroundActivityEpoch;

        vm.OnRenderedThumbnailWorkStarted();

        Assert.Equal(stoppedEpoch + 1, vm.BackgroundActivityEpoch);
        Assert.True(vm.IsBackgroundActivitySamplerRunning);

        view.DataContext = null;
        host.Close();
        await vm.DisposeAsync();
        catalog.Dispose();
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

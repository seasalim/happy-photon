using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class WatermarkPersistenceTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BurstSavesLastValueAfterDelayOrClose(bool closeBeforeDelay)
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var clock = new TestTimeProvider();
        var vm = new MainWindowViewModel(catalog, baseLoader: null, loadMetadataAsync: _ => Task.CompletedTask, timeProvider: clock);
        vm.WorkspaceMode = WorkspaceMode.Export;
        var service = new AppSettingsService(catalog);
        await service.SavePreferencesAsync(new AppSettings { Watermark = new("initial") });
        // test-teardown-policy: allow - ForMainWindow owns binding; finally drains real Closed before catalog teardown.
        var window = new MainWindow();
        typeof(MainWindow).GetField("_appSettingsService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, service);
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var persist = vm.PersistAppSettingsAsync!;
        var saves = 0;
        vm.PersistAppSettingsAsync = async () =>
        {
            await persist();
            saves++;
            saved.TrySetResult();
        };
        try
        {
            for (var i = 0; i < 10; i++)
            {
                vm.ExportSettings.Watermark.Text = $"Text {i}";
                clock.Advance(TimeSpan.FromMilliseconds(100));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("initial", (await service.LoadAsync()).Watermark.Text);
            }
            if (closeBeforeDelay)
            {
                window.Close();
                await closed.Task.WaitAsync(TestWaits.Condition);
            }
            else
            {
                clock.Advance(TimeSpan.FromMilliseconds(149));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("initial", (await service.LoadAsync()).Watermark.Text);
                clock.Advance(TimeSpan.FromMilliseconds(1));
                await saved.Task.WaitAsync(TestWaits.Condition);
                Assert.Equal(1, saves);
            }
            Assert.Equal("Text 9", (await service.LoadAsync()).Watermark.Text);
            clock.Advance(TimeSpan.FromSeconds(1));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Text 9", (await service.LoadAsync()).Watermark.Text);
        }
        finally
        {
            // Keep real shutdown bound and drain it before catalog/VM teardown.
            window.Close();
            await closed.Task.WaitAsync(TestWaits.Condition);
        }
    }

    [AvaloniaFact]
    public async Task CloseWaitsForEarlierSaveAndStoresCurrentSnapshotLast()
    {
        using var root = new TemporaryDirectory();
        using var catalog = new CatalogService(root.Path);
        await catalog.InitializeAsync();
        var clock = new TestTimeProvider();
        var vm = new MainWindowViewModel(catalog, baseLoader: null, loadMetadataAsync: _ => Task.CompletedTask, timeProvider: clock);
        vm.WorkspaceMode = WorkspaceMode.Export;
        var service = new AppSettingsService(catalog);
        // test-teardown-policy: allow - ForMainWindow owns binding; finally drains real Closed before catalog teardown.
        var window = new MainWindow();
        typeof(MainWindow).GetField("_appSettingsService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, service);
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        // Stall the actual catalog write, not the debounce timer.
        var gate = (SemaphoreSlim)typeof(CatalogService)
            .GetField("_connectionGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(catalog)!;
        await gate.WaitAsync();
        try
        {
            vm.ExportSettings.Watermark.Text = "older";
            var earlierSave = vm.PersistAppSettingsAsync!();
            Assert.False(earlierSave.IsCompleted);
            vm.ExportSettings.Watermark.Text = "final";
            window.Close();
            Assert.False(closed.Task.IsCompleted);
            gate.Release();
            await earlierSave.WaitAsync(TestWaits.Condition);
            await closed.Task.WaitAsync(TestWaits.Condition);
            Assert.Equal("final", (await service.LoadAsync()).Watermark.Text);
            clock.Advance(TimeSpan.FromSeconds(1));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("final", (await service.LoadAsync()).Watermark.Text);
        }
        finally
        {
            if (gate.CurrentCount == 0) gate.Release();
            window.Close();
            await closed.Task.WaitAsync(TestWaits.Condition);
        }
    }
}

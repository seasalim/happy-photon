using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BackupNoticeTests
{
    [AvaloniaTheory]
    [InlineData("failed", "The last catalog backup failed; it will retry when you quit.")]
    [InlineData("catalog-damaged", "damaged catalog")]
    [InlineData("ok", null)]
    [InlineData("skipped-disk-space", null)]
    public async Task NextLaunch_ReportsFailuresOnceAfterReady(string status, string? expected)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var outcome = JsonSerializer.Serialize(new BackupOutcome(DateTimeOffset.UtcNow, status));
        using (var setup = new CatalogService(f.Locations.CatalogRoot))
        {
            await setup.InitializeAsync();
            await setup.SetAppSettingAsync(CatalogBackupService.OutcomeKey, outcome);
        }
        for (var launch = 0; launch < 2; launch++)
        {
            using var catalog = new CatalogService();
            await using var vm = new MainWindowViewModel(catalog);
            var window = new MainWindow();
            using var scope = TestUiScope.ForMainWindow(window, vm);
            Assert.Null(vm.StatusMessage);
            await window.InitializeApplicationAsync(vm, catalog, f.Service,
                new CatalogLocationMigrator(f.Service), Path.Combine(directory.Path, "pictures"));
            await vm.BackupNoticeLoad.WaitAsync(TestWaits.Condition);
            Assert.Equal(StartupGateState.Ready, vm.StartupGateState);
            if (launch == 0 && expected != null) Assert.Contains(expected, vm.StatusMessage);
            else Assert.Null(vm.StatusMessage);
            Render(window);
            Assert.Equal(expected == null ? null : outcome, await catalog.GetAppSettingAsync(CatalogBackupService.PresentedOutcomeKey));
            Assert.Null(CatalogRestoreExecutor.ReadNotice(f.Locations.CatalogRoot));
        }
    }

    [AvaloniaTheory]
    [InlineData("failed", false)]
    [InlineData("catalog-damaged", false)]
    [InlineData("failed", true)]
    [InlineData("catalog-damaged", true)]
    public async Task RestoredOutcome_IsAcknowledgedWithoutStatusLine(string status, bool holdNotice)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var outcome = JsonSerializer.Serialize(new BackupOutcome(DateTimeOffset.UtcNow.AddDays(-10), status));
        string backupPath;
        using (var setup = new CatalogService(f.Locations.CatalogRoot))
        {
            await setup.InitializeAsync();
            await setup.SetAppSettingAsync(CatalogBackupService.OutcomeKey, outcome);
            var backup = new CatalogBackupService(setup);
            await backup.BackupAsync();
            backupPath = Assert.Single(backup.List(), item => item.Path + ".zip" != f.Backup).Path + ".zip";
        }
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, backupPath);

        for (var launch = 0; launch < 2; launch++)
        {
            using var catalog = new CatalogService();
            await using var vm = new MainWindowViewModel(catalog);
            var window = new MainWindow();
            using var scope = TestUiScope.ForMainWindow(window, vm);
            var dismiss = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var notices = 0;
            window.ShowRestoreNotice = _ =>
            {
                notices++;
                return holdNotice ? dismiss.Task : Task.CompletedTask;
            };
            try
            {
                await window.InitializeApplicationAsync(vm, catalog, f.Service, journal,
                    Path.Combine(directory.Path, "pictures"));
                await vm.BackupNoticeLoad.WaitAsync(TestWaits.Condition);
                Render(window);
                Assert.Equal(StartupGateState.Ready, vm.StartupGateState);
                Assert.Equal(launch == 0 ? 1 : 0, notices);
                Assert.Null(vm.StatusMessage);
                Assert.Equal(outcome, await catalog.GetAppSettingAsync(CatalogBackupService.PresentedOutcomeKey));
            }
            finally
            {
                dismiss.TrySetResult();
                await TestWaits.UntilAsync(() => CatalogRestoreExecutor.ReadNotice(f.Locations.CatalogRoot) == null);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("pinned")]
    [InlineData("decode")]
    [InlineData("download")]
    [InlineData("transient")]
    public async Task OperationalFeedback_WinsAndDelaysAcknowledgement(string priority)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var outcome = JsonSerializer.Serialize(new BackupOutcome(DateTimeOffset.UtcNow, "failed"));
        await catalog.SetAppSettingAsync(CatalogBackupService.OutcomeKey, outcome);
        await using var vm = new MainWindowViewModel(catalog, new NullBaseLoader(BaseImageLoadFailure.UnsupportedRaw),
            loadMetadataAsync: _ => Task.CompletedTask,
            availabilityService: new TestSourceAvailabilityService(SourceAvailability.AvailableLocally));
        var raw = new ImageFile(Path.Combine(directory.Path, "missing.nef"));
        if (priority is "decode" or "download")
        {
            vm.SelectedImage = raw;
            vm.ApplyPreviewLoadOutcome(new PreviewLoadOutcome(raw, vm.LatestPreviewOutcomeGeneration,
                priority == "decode" ? BaseImageLoadFailure.UnsupportedRaw : BaseImageLoadFailure.SourceUnavailable));
        }
        if (priority == "pinned") vm.PinnedStatus = "Pinned operation";
        if (priority == "transient") vm.TransientStatus = "Transient operation";
        var operational = vm.StatusMessage;
        Assert.NotNull(operational);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await vm.BackupNoticeLoad.WaitAsync(TestWaits.Condition);
        var window = new Window { Width = 1200, Height = 100, Content = new StatusBarView { DataContext = vm } };
        using var scope = new TestUiScope(window);
        Render(window);
        Assert.Equal(operational, vm.StatusMessage);
        Assert.Null(await catalog.GetAppSettingAsync(CatalogBackupService.PresentedOutcomeKey));
        vm.PinnedStatus = null;
        vm.TransientStatus = null;
        vm.SelectedImage = null;
        Render(window);
        Assert.Contains("last catalog backup failed", vm.StatusMessage);
        Assert.Equal(outcome, await catalog.GetAppSettingAsync(CatalogBackupService.PresentedOutcomeKey));
        vm.TransientStatus = "New operation";
        Assert.Equal("New operation", vm.StatusMessage);
        vm.TransientStatus = null;
        Assert.Null(vm.StatusMessage);
    }

    [AvaloniaFact]
    public async Task UndisplayedNotice_IsNotAcknowledged()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        await catalog.SetAppSettingAsync(CatalogBackupService.OutcomeKey,
            JsonSerializer.Serialize(new BackupOutcome(DateTimeOffset.UtcNow, "failed")));
        await using var vm = new MainWindowViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await vm.BackupNoticeLoad.WaitAsync(TestWaits.Condition);
        Assert.NotNull(vm.StatusMessage);
        Assert.Null(await catalog.GetAppSettingAsync(CatalogBackupService.PresentedOutcomeKey));
    }

    [AvaloniaFact]
    public async Task FailedBackup_RendersShowcase()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        await catalog.SetAppSettingAsync(CatalogBackupService.OutcomeKey,
            JsonSerializer.Serialize(new BackupOutcome(DateTimeOffset.UtcNow, "failed")));
        await using var vm = new MainWindowViewModel(catalog);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        await vm.BackupNoticeLoad.WaitAsync(TestWaits.Condition);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm, show: false);
        ShowcaseTestHelper.Capture("status-line-backup-failed", scope, new PixelSize(1200, 700), ThemeVariant.Dark,
            shown => Assert.Contains("last catalog backup failed", vm.StatusMessage));
    }

    private static void Render(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}

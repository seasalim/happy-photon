using System.Diagnostics;
using Avalonia.Headless.XUnit;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BackupStartupGateTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task Everyday_TenRealStartups_DoNotAccessBackupFolderBeforeReady()
    {
        var fixture = await fixtures.Everyday.WaitAsync(TestWaits.Condition);
        var counts = new List<long>();
        var elapsed = new List<double>();
        for (var run = 1; run <= 10; run++)
        {
            var (count, milliseconds) = await LaunchAsync(fixture, positiveControl: false);
            counts.Add(count);
            elapsed.Add(milliseconds);
            output.WriteLine($"G3 launch {run}: {count} folder accesses; {milliseconds:F1} ms");
            Assert.Equal(0, count);
        }
        output.WriteLine($"G3 median: {counts.Order().ElementAt(5)} accesses; " +
            $"10 launches; total startup time {elapsed.Sum():F1} ms; fixture {fixture.SizeMiB:F2} MiB");
    }

    [AvaloniaFact]
    public async Task ListBeforeReady_PositiveControl_IsCounted()
    {
        var fixture = await fixtures.Everyday.WaitAsync(TestWaits.Condition);
        var (count, _) = await LaunchAsync(fixture, positiveControl: true);
        output.WriteLine($"G3 positive control: {count} folder accesses before Ready");
        Assert.True(count > 0);
    }

    private static async Task<(long Count, double Milliseconds)> LaunchAsync(
        BackupCatalogFixture fixture, bool positiveControl)
    {
        var before = CatalogBackupService.FolderAccessCount;
        using var directory = new TemporaryDirectory();
        var pictures = Directory.CreateDirectory(Path.Combine(directory.Path, "pictures")).FullName;
        var locations = new AppDataLocationService(new AppDataPlatformPaths(
            pictures, Path.Combine(directory.Path, "pointer"),
            Path.Combine(directory.Path, "catalog"), Path.Combine(directory.Path, "cache")));
        var resolved = await locations.CreateFreshAsync(useStandardCatalog: true);
        File.Copy(fixture.DatabasePath, resolved.DatabasePath);
        File.Copy(Path.Combine(fixture.Root, ".catalog-identity"),
            Path.Combine(resolved.CatalogRoot, ".catalog-identity"));
        // Only startup preferences change on this private copy; fixture rows/history stay intact.
        using (var setup = new CatalogService(resolved.CatalogRoot))
        {
            await setup.InitializeAsync();
            await setup.SetAppSettingsAsync(new Dictionary<string, string?>
            {
                ["FirstRunExperienceVersion"] = MainWindowViewModel.CurrentFirstRunExperienceVersion.ToString(),
                ["RootFolderPath"] = pictures,
                ["SelectedFolderPath"] = pictures
            });
        }
        var migrator = new CatalogLocationMigrator(locations);
        Assert.False(File.Exists(migrator.JournalPath));
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var clock = Stopwatch.StartNew();
        try
        {
            Assert.NotEqual(StartupGateState.Ready, vm.StartupGateState);
            if (positiveControl)
            {
                using var controlCatalog = new CatalogService(resolved.CatalogRoot);
                new CatalogBackupService(controlCatalog).List();
                Assert.True(CatalogBackupService.FolderAccessCount > before);
            }
            // This is the production startup entry used by App.CompleteStartupAsync:
            // RAW health, pending journal, locations, catalog/schema, presets and session restore.
            await window.InitializeApplicationAsync(vm, catalog, locations, migrator, pictures)
                .WaitAsync(TestWaits.Condition);
            Assert.True(vm.StartupGateState == StartupGateState.Ready, vm.FirstRunErrorMessage);
            Assert.Equal(resolved.CatalogRoot, catalog.CatalogPath);
            return (CatalogBackupService.FolderAccessCount - before, clock.Elapsed.TotalMilliseconds);
        }
        finally
        {
            scope.Dispose();
            await vm.DisposeAsync().AsTask().WaitAsync(TestWaits.Condition);
        }
    }
}

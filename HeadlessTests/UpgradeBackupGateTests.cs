using System.Diagnostics;
using Avalonia.Headless.XUnit;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class UpgradeBackupGateTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task Schema3_FivePairedLaunches_BoundAddedBackupTime()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 under the exclusive measure lock for WP4 G1.");
        var fixture = await fixtures.Schema3Archive;
        Assert.InRange(fixture.SizeMiB, 160, 260);
        var withBackup = new List<double>();
        var withoutBackup = new List<double>();

        for (var pair = 0; pair < 5; pair++)
        {
            foreach (var enabled in pair % 2 == 0 ? new[] { true, false } : new[] { false, true })
            {
                var elapsed = await Launch(fixture, enabled);
                (enabled ? withBackup : withoutBackup).Add(elapsed);
                output.WriteLine($"G1 pair={pair + 1} backup={enabled} ms={elapsed:F3}");
            }
        }

        // The envelope's host-speed reference is the archive copy control, separate from the
        // "without" median that the added time is measured against.
        var copyControl = await MeasureCopyControlAsync();
        var without = withoutBackup.Order().ElementAt(2);
        var protectedLaunch = withBackup.Order().ElementAt(2);
        var added = protectedLaunch - without;
        output.WriteLine($"G1 fixture_mib={fixture.SizeMiB:F3} copy_control_ms={copyControl:F3} " +
            $"without_ms={without:F3} with_ms={protectedLaunch:F3} added_ms={added:F3} limit_ms=6000");

        Assert.InRange(copyControl, 100, 1000);
        Assert.True(added <= 6000, $"Backup added {added:F3} ms.");
    }

    private async Task<double> MeasureCopyControlAsync()
    {
        var archive = await fixtures.Archive;
        var samples = new List<double>();
        using var directory = new TemporaryDirectory();
        var target = Path.Combine(directory.Path, "copy-control.db");

        for (var run = 0; run < 5; run++)
        {
            var timer = Stopwatch.StartNew();

            using (var source = new FileStream(archive.DatabasePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
            using (var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
            {
                source.CopyTo(destination, 1024 * 1024);
                destination.Flush(flushToDisk: true);
            }

            samples.Add(timer.Elapsed.TotalMilliseconds);
            File.Delete(target); // Deletion is outside the measured interval.
        }

        return samples.Order().ElementAt(2);
    }

    private static async Task<double> Launch(BackupCatalogFixture fixture, bool enabled)
    {
        using var directory = new TemporaryDirectory();
        var service = RestoreTestSupport.Service(directory.Path);
        var locations = await service.CreateFreshAsync(useStandardCatalog: true);
        File.Copy(fixture.DatabasePath, locations.DatabasePath);
        CatalogCacheStamp.EnsureIdentity(locations.CatalogRoot, out _);

        using (var copy = CatalogService.OpenBackupCopy(locations.DatabasePath, SqliteOpenMode.ReadWrite))
        {
            using var command = copy.CreateCommand();
            command.CommandText = "INSERT INTO app_settings VALUES ('FirstRunExperienceVersion', '1');";
            command.ExecuteNonQuery();
        }

        using var catalog = new CatalogService { SkipUpgradeBackupForTests = !enabled };
        await using var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var clock = Stopwatch.StartNew();
        await window.InitializeApplicationAsync(vm, catalog, service, new CatalogLocationMigrator(service), null)
            .WaitAsync(TestWaits.Condition);
        clock.Stop();
        Assert.True(vm.StartupGateState == StartupGateState.Ready, vm.FirstRunErrorMessage);
        Assert.Equal("4", await catalog.GetAppSettingAsync("schema_version"));
        var backup = new CatalogBackupService(catalog);

        if (enabled)
        {
            using var verified = backup.CheckForRestore(Assert.Single(backup.ListForRestore()).Path);
            Assert.Equal("before-upgrade", verified.Manifest.Kind);
            Assert.Equal(3, verified.Manifest.SchemaVersion);
            Assert.Equal(fixture.ImageRows, verified.Manifest.ImageRows);
        }
        else
        {
            Assert.Empty(backup.ListForRestore());
        }

        return clock.Elapsed.TotalMilliseconds;
    }
}

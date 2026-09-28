using System.Diagnostics;
using Avalonia.Headless.XUnit;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RestoreHotJournalStartupTests(BackupCatalogFixtures fixtures)
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KilledWriter_ReachesReadyWithPreTransactionRows(bool restorePreservation)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path, await fixtures.Everyday);
        var expectedRows = ReadRows(f.Locations.DatabasePath);
        var expectedDatabaseHash = CatalogBackupService.Hash(f.Locations.DatabasePath);
        var expectedIdentity = CatalogBackupService.ReadIdentity(f.Locations.CatalogRoot);
        await KillWriter(f.Locations.DatabasePath);
        var journalPath = f.Locations.DatabasePath + "-journal";
        Assert.True(new FileInfo(journalPath).Length > 512);
        Assert.Equal(new byte[] { 0xd9, 0xd5, 0x05, 0xf9, 0x20, 0xa1, 0x63, 0xd7 },
            File.ReadAllBytes(journalPath)[..8]);
        Assert.NotEqual(expectedDatabaseHash, CatalogBackupService.Hash(f.Locations.DatabasePath));
        Assert.False(AppDataLocationService.HasCatalogSignature(f.Locations.CatalogRoot));
        var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        var store = new CatalogLocationMigrator(f.Service);
        string? preservation = null;
        if (restorePreservation)
        {
            await new CatalogRestoreExecutor(store).StageAsync(f.Locations, f.Backup);
            await store.ExecutePendingAsync();
            RestoreTestSupport.AssertPreserved(f, original, damaged: false);
            using var listingCatalog = new CatalogService(f.Locations.CatalogRoot);
            preservation = Assert.Single(new CatalogBackupService(listingCatalog).ListForRestore(),
                row => row.Path != f.Backup).Path;
            await new CatalogRestoreExecutor(store).StageAsync(f.Locations, preservation);
            store.RestoreStep = step =>
            {
                if (step == "after:journal-delete")
                    Assert.Equal(original, RestoreTestSupport.Generation(f.Locations.CatalogRoot));
            };
        }

        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var notices = new List<string>();
        window.ShowRestoreNotice = message => { notices.Add(message); return Task.CompletedTask; };
        try
        {
            await window.InitializeApplicationAsync(vm, catalog, f.Service, store,
                Path.Combine(directory.Path, "pictures")).WaitAsync(TestWaits.Condition);
            Assert.True(vm.StartupGateState == StartupGateState.Ready, vm.FirstRunErrorMessage);
            Assert.Equal(expectedIdentity, catalog.OpenCatalogIdentity);
            Assert.Equal(expectedRows, ReadRows(f.Locations.DatabasePath));
            Assert.False(File.Exists(journalPath));
            Assert.False(File.Exists(store.JournalPath));
            if (restorePreservation) Assert.Contains(preservation!, Assert.Single(notices));
            else Assert.Empty(notices);
            Assert.Null(CatalogRestoreExecutor.ReadNotice(f.Locations.CatalogRoot));
        }
        finally { scope.Dispose(); await vm.DisposeAsync(); }
        catalog.Dispose();
        Assert.Equal(expectedDatabaseHash, CatalogBackupService.Hash(f.Locations.DatabasePath));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenuinelyCorruptCatalog_ReachesErrorScreen(bool hasJournal)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        File.WriteAllText(f.Locations.DatabasePath, "not SQLite");
        if (hasJournal) File.WriteAllText(f.Locations.DatabasePath + "-journal", "invalid journal");
        using var catalog = new CatalogService();
        var vm = new MainWindowViewModel(catalog);
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        try
        {
            await window.InitializeApplicationAsync(vm, catalog, f.Service, new CatalogLocationMigrator(f.Service),
                Path.Combine(directory.Path, "pictures")).WaitAsync(TestWaits.Condition);
            Assert.True(vm.IsStartupError);
            Assert.Equal("The local catalog is damaged or unrecognized. Restore a backup or retry.",
                vm.FirstRunErrorMessage);
            Assert.Null(catalog.OpenCatalogIdentity);
            Assert.Equal("not SQLite", File.ReadAllText(f.Locations.DatabasePath));
        }
        finally { scope.Dispose(); await vm.DisposeAsync(); }
    }

    private static List<(long Id, string Name)> ReadRows(string database)
    {
        using var connection = CatalogService.OpenBackupCopy(database, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, file_name FROM images ORDER BY id;";
        using var reader = command.ExecuteReader();
        var rows = new List<(long, string)>();
        while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1)));
        return rows;
    }

    internal static async Task KillWriter(string database)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { typeof(RestoreHotJournalStartupTests).Assembly.Location, "-method",
                     "HappyPhoton.Tests.RestoreHotJournalStartupTests.Child", "-showLiveOutput", "-noColor" })
            start.ArgumentList.Add(argument);
        start.Environment["HP_STARTUP_HOT_CHILD"] = database;
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TestWaits.Condition);
        var signalled = false;
        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
                if (line == "HOT_JOURNAL_READY") { signalled = true; break; }
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        Assert.True(signalled, await error);
        Assert.NotEqual(0, process.ExitCode);
    }

    [Fact]
    public void Child()
    {
        var database = Environment.GetEnvironmentVariable("HP_STARTUP_HOT_CHILD");
        Assert.SkipWhen(database == null, "Only the termination parent invokes this test.");
        using var connection = CatalogService.OpenBackupCopy(database!, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA cache_size=1;
            PRAGMA cache_spill=ON;
            BEGIN IMMEDIATE;
            UPDATE images SET file_name=printf('%0500d', id) WHERE id <= 100;
            """;
        command.ExecuteNonQuery();
        using var signal = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        signal.WriteLine("HOT_JOURNAL_READY");
        using var wait = new ManualResetEventSlim();
        Assert.True(wait.Wait(TestWaits.Condition), "Parent failed to terminate child.");
    }
}

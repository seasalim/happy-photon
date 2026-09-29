using System.Diagnostics;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(BackupBaselineCollection.Name)]
public sealed class RestoreHotJournalTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    [Fact]
    public async Task HotRollbackJournal_IsPreservedWithoutRecoveringTheOriginal()
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path, await fixtures.Everyday);
        var recoveredDatabaseHash = CatalogBackupService.Hash(f.Locations.DatabasePath);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { typeof(RestoreHotJournalTests).Assembly.Location, "-method",
                     "HappyPhoton.Tests.RestoreHotJournalTests.Child", "-showLiveOutput", "-noColor" })
            start.ArgumentList.Add(argument);
        start.Environment["HP_RESTORE_HOT_CHILD"] = f.Locations.DatabasePath;
        using var process = Process.Start(start)!;
        var childPid = process.Id;
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
            await BackupCrashFileRelease.WaitAsync(f.Locations.CatalogRoot, childPid, output);
        }

        Assert.True(signalled, await error);
        var journalPath = f.Locations.DatabasePath + "-journal";
        Assert.True(new FileInfo(journalPath).Length > 512);
        Assert.Equal(new byte[] { 0xd9, 0xd5, 0x05, 0xf9, 0x20, 0xa1, 0x63, 0xd7 },
            File.ReadAllBytes(journalPath)[..8]);
        var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        var store = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(store).StageAsync(f.Locations, f.Backup);
        Assert.Equal(original, RestoreTestSupport.Generation(f.Locations.CatalogRoot));
        await store.ExecutePendingAsync();
        Assert.Equal(RestoreTestSupport.Payload(f.Backup), RestoreTestSupport.Generation(f.Locations.CatalogRoot));
        RestoreTestSupport.AssertPreserved(f, original, damaged: false);
        using var listingCatalog = new CatalogService(f.Locations.CatalogRoot);
        var backups = new CatalogBackupService(listingCatalog);
        var preservation = Assert.Single(backups.ListForRestore(), row => row.Path != f.Backup);
        Assert.True(preservation.CanRestore);
        var archiveHash = CatalogBackupService.Hash(preservation.Path);
        using (var check = backups.CheckForRestore(preservation.Path))
            foreach (var (name, hash) in original)
                Assert.Equal(hash, CatalogBackupService.Hash(Path.Combine(check.Directory, name)));
        await new CatalogRestoreExecutor(store).StageAsync(f.Locations, preservation.Path);
        await store.ExecutePendingAsync();
        Assert.Equal(original, RestoreTestSupport.Generation(f.Locations.CatalogRoot));
        Assert.Equal(archiveHash, CatalogBackupService.Hash(preservation.Path));
        using (var recovered = CatalogService.OpenBackupCopy(f.Locations.DatabasePath, SqliteOpenMode.ReadWrite))
        {
            using var check = recovered.CreateCommand();
            check.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", check.ExecuteScalar());
        }
        Assert.False(File.Exists(journalPath));
        Assert.Equal(recoveredDatabaseHash, CatalogBackupService.Hash(f.Locations.DatabasePath));
    }

    [Fact]
    public void Child()
    {
        var database = Environment.GetEnvironmentVariable("HP_RESTORE_HOT_CHILD");
        Assert.SkipWhen(database == null, "Only the termination parent invokes this test.");
        using var connection = CatalogService.OpenBackupCopy(database!, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA cache_size=1;
            PRAGMA cache_spill=ON;
            BEGIN IMMEDIATE;
            UPDATE images SET file_name=printf('%0500d', id);
            """;
        command.ExecuteNonQuery();
        using var signal = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        signal.WriteLine("HOT_JOURNAL_READY");
        using var wait = new ManualResetEventSlim();
        Assert.True(wait.Wait(TestWaits.Condition), "Parent failed to terminate child.");
    }
}

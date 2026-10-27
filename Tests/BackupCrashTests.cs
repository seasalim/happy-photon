using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(BackupBaselineCollection.Name)]
public sealed class BackupCrashTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    public static IEnumerable<object[]> KillPoints => BackupKillPointMatrix.Backup.Rows();

    [Theory]
    [MemberData(nameof(KillPoints))]
    public async Task RealTermination_PreservesLiveCatalogAndCommitMarkers(string point)
    {
        var matrix = BackupKillPointMatrix.Backup;
        var discovered = await matrix.DiscoverAsync(fixtures);

        foreach (var target in matrix.Targets(point, discovered))
        {
            await KillAtAsync(target, discovered);
        }
    }

    private async Task KillAtAsync(string point, string[] discovered)
    {
        var archiveGate = Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") == "1";
        var fixture = await (archiveGate ? fixtures.Archive : fixtures.Everyday);
        Assert.InRange(fixture.SizeMiB, archiveGate ? 160 : 1.5, archiveGate ? 260 : 4);
        output.WriteLine($"fixture={(archiveGate ? "archive G4" : "everyday")} size_mib={fixture.SizeMiB:F3}");
        var childPid = 0;
        using var directory = new TemporaryDirectory();
        directory.CleanupFailure = exception =>
            BackupCrashLockDiagnostics.Cleanup(exception, directory.Path, childPid, output);
        await BackupCrashDriver.PrepareAsync(directory.Path, fixture);
        var initialZips = Directory.GetFiles(Path.Combine(directory.Path, "Backups"), "*.zip")
            .ToHashSet(StringComparer.Ordinal);
        using var child = new BackupKillPointProcess(BackupKillPointMatrix.Backup,
            directory.Path, point, discovered, output);
        var process = child.Process;
        childPid = process.Id;
        var expectedHash = await child.KillAsync();
        Assert.NotNull(expectedHash);

        Assert.NotEqual(0, process.ExitCode);
        Assert.Equal(expectedHash, BackupTestSupport.Hash(Path.Combine(directory.Path, "catalog.db")));
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog);
        using var diagnostics = new BackupCrashDiagnostics(catalog, service, process, output);
        var listed = service.List();
        var initiallyDue = service.IsDue();
        await diagnostics.ReportReadFailuresAsync();

        foreach (var sidecar in Directory.GetFiles(service.Folder, "*.manifest.json"))
            Assert.True(File.Exists(sidecar[..^14] + ".zip"));

        var fresh = listed.Where(item => !initialZips.Contains(item.Path + ".zip")).ToArray();
        Assert.Equal(fresh.Length == 0, initiallyDue);

        foreach (var zip in Directory.GetFiles(service.Folder, "*.zip").Where(path => !path.Contains(".partial.") && !initialZips.Contains(path)))
        {
            using var restore = new TemporaryDirectory();
            restore.CleanupFailure = exception =>
                BackupCrashLockDiagnostics.Cleanup(exception, restore.Path, childPid, output);
            await BackupTestSupport.AssertArchiveAsync(zip, restore.Path);
            AssertConcurrentTransactions(Path.Combine(restore.Path, "catalog.db"));
        }

        var uncheckedZips = Directory.GetFiles(service.Folder, "*.zip")
            .Where(path => !path.Contains(".partial.") && !File.Exists(path[..^4] + ".manifest.json"))
            .ToDictionary(path => path, BackupTestSupport.Hash);
        // A fresh attempt must ignore unchecked zips and finish interrupted retention.
        diagnostics.BeginAttempt();
        await service.BackupAsync();
        var count = service.List().Count;
        var due = service.IsDue();

        if (count != 5 || due)
        {
            await diagnostics.ReportAsync($"fresh count={count} due={due}");
        }
        else
        {
            await diagnostics.ReportReadFailuresAsync();
        }

        Assert.Equal(5, count);
        Assert.False(due);

        foreach (var (path, hash) in uncheckedZips) Assert.Equal(hash, BackupTestSupport.Hash(path));

        Assert.Empty(Directory.GetFiles(service.Folder, "*.partial.*"));
        output.WriteLine($"{point}: real child killed; live bytes, publication, transactions, presets, due and retention passed");
    }

    private static void AssertConcurrentTransactions(string database)
    {
        using var copy = CatalogService.OpenBackupCopy(database, Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly);
        using var command = copy.CreateCommand();
        // All writes were issued while the gate was held, after snapshot facts were read.
        // They must be wholly absent, including their history and assessment revisions.
        command.CommandText = "SELECT count(*) FROM edit_history WHERE label IN ('Backup autosave', 'Backup paste');";
        Assert.Equal(0L, command.ExecuteScalar());
        command.CommandText = "SELECT count(*) FROM image_assessments WHERE image_id IN (4,5) AND revision <> 1;";
        Assert.Equal(0L, command.ExecuteScalar());
        command.CommandText = "SELECT edit_settings FROM images WHERE id IN (1,2,3);";
        using var reader = command.ExecuteReader();

        while (reader.Read())
            Assert.DoesNotContain(EditSettingsJson.Deserialize(reader.GetString(0), out _).Exposure, new[] { 1.25, 2.25 });
    }
}

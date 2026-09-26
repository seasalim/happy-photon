using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(BackupBaselineCollection.Name)]
public sealed class BackupCrashTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    public static TheoryData<string> KillPoints => new(
        new[] { "sweep", "snapshot", "zip", "sidecar", "publish-zip", "publish-sidecar",
            "snapshot-delete", "prune-sidecar", "prune-zip", "outcome",
            "preset-write", "preset-rename", "preset-delete" }
        .SelectMany(name => new[] { "before:" + name, "after:" + name }));

    [Theory]
    [MemberData(nameof(KillPoints))]
    public async Task RealTermination_PreservesLiveCatalogAndCommitMarkers(string point)
    {
        var archiveGate = Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") == "1";
        var fixture = await (archiveGate ? fixtures.Archive : fixtures.Everyday);
        Assert.InRange(fixture.SizeMiB, archiveGate ? 160 : 1.5, archiveGate ? 260 : 4);
        output.WriteLine($"fixture={(archiveGate ? "archive G4" : "everyday")} size_mib={fixture.SizeMiB:F3}");
        using var directory = new TemporaryDirectory();
        BackupTestSupport.CopyCatalog(fixture.Root, directory.Path);
        using (var seedCatalog = new CatalogService(directory.Path))
        {
            await seedCatalog.InitializeAsync();
            var seed = new CatalogBackupService(seedCatalog) { UtcNow = () => DateTimeOffset.UtcNow.AddDays(-20) };
            var presets = new PresetService(Path.Combine(directory.Path, "presets"));
            await presets.InitializeAsync();
            await presets.SaveUserPresetAsync("Concurrent", new EditSettings { Exposure = 1 });
            await seed.BackupAsync();
            var item = Assert.Single(seed.List());
            for (var i = 0; i < 4; i++)
            {
                var stem = Path.Combine(seed.Folder, $"hp-backup-{Guid.NewGuid():N}");
                File.Copy(item.Path + ".zip", stem + ".zip");
                File.Copy(item.Path + ".manifest.json", stem + ".manifest.json");
            }
            File.WriteAllText(Path.Combine(seed.Folder, $"hp-backup-{Guid.NewGuid():N}.partial.db"), "stale");
        }
        var initialZips = Directory.GetFiles(Path.Combine(directory.Path, "Backups"), "*.zip")
            .ToHashSet(StringComparer.Ordinal);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { typeof(BackupCrashTests).Assembly.Location,
                     "-method", "HappyPhoton.Tests.BackupCrashTests.Child", "-showLiveOutput", "-noColor" })
            start.ArgumentList.Add(argument);
        start.Environment["HAPPY_PHOTON_BACKUP_CHILD"] = directory.Path;
        start.Environment["HAPPY_PHOTON_BACKUP_KILL"] = point;
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        string? expectedHash = null;
        using var timeout = new CancellationTokenSource(TestWaits.Condition);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                const string prefix = "BACKUP_KILL_READY:";
                if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
                expectedHash = line[prefix.Length..];
                break;
            }
            Assert.NotNull(expectedHash);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        Assert.NotEqual(0, process.ExitCode);
        Assert.Equal(expectedHash, BackupTestSupport.Hash(Path.Combine(directory.Path, "catalog.db")));
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog);
        var listed = service.List();
        foreach (var sidecar in Directory.GetFiles(service.Folder, "*.manifest.json"))
            Assert.True(File.Exists(sidecar[..^14] + ".zip"));
        var fresh = listed.Where(item => !initialZips.Contains(item.Path + ".zip")).ToArray();
        Assert.Equal(fresh.Length == 0, service.IsDue());
        foreach (var zip in Directory.GetFiles(service.Folder, "*.zip").Where(path => !path.Contains(".partial.") && !initialZips.Contains(path)))
        {
            using var restore = new TemporaryDirectory();
            await BackupTestSupport.AssertArchiveAsync(zip, restore.Path);
            AssertConcurrentTransactions(Path.Combine(restore.Path, "catalog.db"));
        }
        var uncheckedZips = Directory.GetFiles(service.Folder, "*.zip")
            .Where(path => !path.Contains(".partial.") && !File.Exists(path[..^4] + ".manifest.json"))
            .ToDictionary(path => path, BackupTestSupport.Hash);
        // A fresh attempt must ignore unchecked zips and finish interrupted retention.
        await service.BackupAsync();
        Assert.Equal(5, service.List().Count);
        Assert.False(service.IsDue());
        foreach (var (path, hash) in uncheckedZips) Assert.Equal(hash, BackupTestSupport.Hash(path));
        Assert.Empty(Directory.GetFiles(service.Folder, "*.partial.*"));
        output.WriteLine($"{point}: real child killed; live bytes, publication, transactions, presets, due and retention passed");
    }

    [Fact]
    public async Task Child()
    {
        var root = Environment.GetEnvironmentVariable("HAPPY_PHOTON_BACKUP_CHILD");
        Assert.SkipWhen(root == null, "Only the termination parent invokes this test.");
        var target = Environment.GetEnvironmentVariable("HAPPY_PHOTON_BACKUP_KILL");
        using var catalog = new CatalogService(root!);
        await catalog.InitializeAsync();
        var presets = new PresetService(Path.Combine(root!, "presets"));
        await presets.InitializeAsync();
        var preset = Assert.Single(presets.UserPresets);
        Task writes = Task.CompletedTask;
        var expectedHash = BackupTestSupport.Hash(Path.Combine(root!, "catalog.db"));
        var service = new CatalogBackupService(catalog);
        service.Step = point =>
        {
            if (point == "before:snapshot")
            {
                Assert.Equal(expectedHash, BackupTestSupport.Hash(Path.Combine(root!, "catalog.db")));
                writes = WriteAndHashAsync();
            }
            if (point == "after:snapshot")
                presets.SaveUserPresetAsync("Concurrent", new EditSettings { Exposure = 2 }, preset.Id).GetAwaiter().GetResult();
            if (point == "before:zip") writes.GetAwaiter().GetResult();
            if (point == "after:outcome") expectedHash = BackupTestSupport.Hash(Path.Combine(root!, "catalog.db"));
            KillAt(point);
        };
        presets.WriteStep = KillAt;
        void KillAt(string point)
        {
            if (point != target) return;
            using var signal = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Assert.Equal(expectedHash, BackupTestSupport.Hash(Path.Combine(root!, "catalog.db")));
            signal.WriteLine("BACKUP_KILL_READY:" + expectedHash);
            using var wait = new ManualResetEventSlim();
            Assert.True(wait.Wait(TestWaits.Condition), "Parent failed to terminate child.");
        }
        async Task WriteAndHashAsync()
        {
            await BackupTestSupport.WriteConcurrentAsync(catalog);
            expectedHash = BackupTestSupport.Hash(Path.Combine(root!, "catalog.db"));
        }
        await service.BackupAsync();
        Assert.Fail("Kill point was not reached: " + target);
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

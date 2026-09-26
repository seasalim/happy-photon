using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogBackupTests(BackupCatalogFixtures fixtures)
{
    [Theory]
    [InlineData(65001, false)]
    [InlineData(65001, true)]
    [InlineData(1200, true)]
    [InlineData(1201, true)]
    [InlineData(12000, true)]
    [InlineData(12001, true)]
    public async Task EncodedPresetIsCapturedUnchangedAndLoadsAfterRestore(int codePage, bool withBom)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var presetDirectory = Path.Combine(directory.Path, "presets");
        var presets = new PresetService(presetDirectory);
        await presets.InitializeAsync();
        var saved = await presets.SaveUserPresetAsync("Lumière", new EditSettings { Exposure = 1, Contrast = 17 });
        var path = Path.Combine(presetDirectory, saved.Id + ".json");
        var encoding = Encoding.GetEncoding(codePage);
        var json = await File.ReadAllTextAsync(path);
        byte[] original = [.. withBom ? encoding.GetPreamble() : [], .. encoding.GetBytes(json)];
        await File.WriteAllBytesAsync(path, original);
        var loaded = new PresetService(presetDirectory);
        await loaded.InitializeAsync();
        var expected = Assert.Single(loaded.UserPresets);
        Assert.Equal(saved.Id, expected.Id);
        Assert.Equal(saved.Settings.Exposure, expected.Settings.Exposure);
        Assert.Equal(saved.Settings.Contrast, expected.Settings.Contrast);

        var service = new CatalogBackupService(catalog);
        await service.BackupAsync();

        Assert.Equal("ok", (await Outcome(catalog)).Status);
        var item = Assert.Single(service.List());
        Assert.Contains("presets/" + saved.Id + ".json", item.Manifest.Entries.Keys);
        using var restore = new TemporaryDirectory();
        await BackupTestSupport.AssertArchiveAsync(item.Path + ".zip", restore.Path);
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(restore.Path, "presets", saved.Id + ".json")));
        var restored = new PresetService(Path.Combine(restore.Path, "presets"));
        await restored.InitializeAsync();
        var actual = Assert.Single(restored.UserPresets);
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(EditSettingsJson.Serialize(expected.Settings), EditSettingsJson.Serialize(actual.Settings));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"version\":999}")]
    [InlineData("{\"version\":2,\"name\":\"Missing id\",\"settings\":{}}")]
    [InlineData("{\"version\":2,\"id\":\"missing-name\",\"settings\":{}}")]
    public async Task InvalidPresetIsSkippedWhileValidPresetIsCaptured(string invalidJson)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var presetDirectory = Path.Combine(directory.Path, "presets");
        var presets = new PresetService(presetDirectory);
        await presets.InitializeAsync();
        var valid = await presets.SaveUserPresetAsync("Valid", new EditSettings { Exposure = 1 });
        File.WriteAllText(Path.Combine(presetDirectory, "invalid.json"), invalidJson);
        var loaded = new PresetService(presetDirectory);
        await loaded.InitializeAsync();
        Assert.Equal(valid.Id, Assert.Single(loaded.UserPresets).Id);

        var service = new CatalogBackupService(catalog);
        await service.BackupAsync();

        Assert.Equal("ok", (await Outcome(catalog)).Status);
        var item = Assert.Single(service.List());
        Assert.DoesNotContain("presets/invalid.json", item.Manifest.Entries.Keys);
        using var restore = new TemporaryDirectory();
        await BackupTestSupport.AssertArchiveAsync(item.Path + ".zip", restore.Path);
        var restored = new PresetService(Path.Combine(restore.Path, "presets"));
        await restored.InitializeAsync();
        Assert.Equal(valid.Id, Assert.Single(restored.UserPresets).Id);
    }

    [Theory]
    [InlineData("unc")]
    [InlineData("io")]
    [InlineData("access")]
    public async Task UnknownFreeSpaceStillProducesVerifiedBackup(string fault)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog)
        {
            FreeSpace = _ => throw (fault switch
            {
                "unc" => new ArgumentException("UNC drive information unavailable"),
                "io" => new IOException("Drive information unavailable"),
                _ => new UnauthorizedAccessException("Drive information denied")
            })
        };

        await service.BackupAsync();

        Assert.Equal("ok", (await Outcome(catalog)).Status);
        using var restore = new TemporaryDirectory();
        await BackupTestSupport.AssertArchiveAsync(Assert.Single(service.List()).Path + ".zip", restore.Path);
    }

    [Fact]
    public async Task VerifiedBackup_RestoresEveryTable_AndNotDueOnlyLists()
    {
        using var directory = new TemporaryDirectory();
        BackupTestSupport.CopyCatalog((await fixtures.Everyday).Root, directory.Path);
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var presets = new PresetService(Path.Combine(directory.Path, "presets"));
        await presets.InitializeAsync();
        await presets.SaveUserPresetAsync("First", new EditSettings { Exposure = 1 });
        var expected = BackupTestSupport.Rows(Path.Combine(directory.Path, "catalog.db"));
        var service = new CatalogBackupService(catalog);
        await service.BackupIfDueAsync();
        var backup = Assert.Single(service.List());
        using var restore = new TemporaryDirectory();
        await BackupTestSupport.AssertArchiveAsync(backup.Path + ".zip", restore.Path);
        BackupTestSupport.EqualRows(expected, Path.Combine(restore.Path, "catalog.db"));
        Assert.Equal("ok", (await Outcome(catalog)).Status);
        var before = Directory.GetFiles(service.Folder).ToDictionary(path => path, BackupTestSupport.Hash);
        var database = BackupTestSupport.Hash(Path.Combine(directory.Path, "catalog.db"));
        var accesses = new List<string>();
        service.Step = accesses.Add;
        using (var lockedArchive = new FileStream(backup.Path + ".zip", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await service.BackupIfDueAsync();
        Assert.Equal(["list"], accesses);
        Assert.Equal(database, BackupTestSupport.Hash(Path.Combine(directory.Path, "catalog.db")));
        Assert.Equal(before, Directory.GetFiles(service.Folder).ToDictionary(path => path, BackupTestSupport.Hash));
    }

    [Theory]
    [InlineData("disk", "skipped-disk-space")]
    [InlineData("write", "failed")]
    [InlineData("corrupt", "catalog-damaged")]
    public async Task OutcomesAreRecorded(string fault, string status)
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog);
        if (fault == "disk") service.FreeSpace = _ => 0;
        service.Step = step =>
        {
            if (fault == "write" && step == "before:zip") throw new IOException("Injected write failure");
            if (fault == "corrupt" && step == "after:snapshot")
            {
                var path = Assert.Single(Directory.GetFiles(service.Folder, "*.partial.db"));
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(new byte[100]);
            }
        };
        await service.BackupAsync();
        var outcome = await Outcome(catalog);
        Assert.Equal(status, outcome.Status);
        Assert.True(outcome.Utc > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Empty(service.List());
        Assert.NotNull(await catalog.GetAppSettingAsync("schema_version"));
    }

    [Fact]
    public async Task UnopenedCatalogDoesNoIo()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(Path.Combine(directory.Path, "unopened"));
        var service = new CatalogBackupService(catalog) { Step = _ => Assert.Fail("Unexpected I/O") };
        await service.BackupIfDueAsync();
        Assert.False(Directory.Exists(catalog.CatalogPath));
    }

    [Fact]
    public async Task RetentionAndSweepOnlyDeleteOwnedEligibleFiles()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog);
        var time = DateTimeOffset.UtcNow.AddDays(-20);
        service.UtcNow = () => time;
        await service.BackupAsync("before-restore");
        await service.BackupAsync("before-upgrade");
        var preserved = service.List().Select(item => item.Path + ".zip").ToList();
        Directory.CreateDirectory(service.Folder);
        var unknown = Path.Combine(service.Folder, "unknown.zip");
        File.WriteAllText(unknown, "keep");
        preserved.Add(unknown);
        var uncheckedZip = Path.Combine(service.Folder, $"hp-backup-{Guid.NewGuid():N}.zip");
        File.WriteAllText(uncheckedZip, "unchecked");
        preserved.Add(uncheckedZip);
        var unrelatedPartial = Path.Combine(service.Folder, "my.partial.db");
        File.WriteAllText(unrelatedPartial, "keep");
        preserved.Add(unrelatedPartial);
        var stale = Path.Combine(service.Folder, $"hp-backup-{Guid.NewGuid():N}.partial.db");
        File.WriteAllText(stale, "stale");
        var all = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            time = time.AddDays(1);
            await service.BackupAsync(i % 2 == 0 ? "scheduled" : "manual");
            all.Add(service.List().MaxBy(item => item.Manifest.Utc).Path);
        }
        Assert.False(File.Exists(stale));
        Assert.All(preserved, path => Assert.True(File.Exists(path), path));
        Assert.Equal(7, service.List().Count);
        Assert.All(all.Take(2), path => Assert.False(File.Exists(path + ".zip")));
        Assert.All(all.Skip(2), path => Assert.True(File.Exists(path + ".zip")));
        time = DateTimeOffset.UtcNow;
        Assert.True(service.IsDue());
        File.Delete(Path.Combine(directory.Path, AppDataRootOwnership.MarkerFileName));
        await service.BackupAsync();
        Assert.Equal("failed", (await Outcome(catalog)).Status);
        Assert.All(preserved, path => Assert.True(File.Exists(path)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentTransactionsAndPresetsAreWhole_AndCallersJoin(bool writesFirst)
    {
        using var directory = new TemporaryDirectory();
        BackupTestSupport.CopyCatalog((await fixtures.Everyday).Root, directory.Path);
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var presets = new PresetService(Path.Combine(directory.Path, "presets"));
        await presets.InitializeAsync();
        var preset = await presets.SaveUserPresetAsync("Concurrent", new EditSettings { Exposure = 1 });
        if (writesFirst) await BackupTestSupport.WriteConcurrentAsync(catalog);
        var expected = BackupTestSupport.Rows(Path.Combine(directory.Path, "catalog.db"));
        using var reached = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        Task writes = Task.CompletedTask;
        var service = new CatalogBackupService(catalog);
        service.Step = step =>
        {
            if (step != "before:snapshot") return;
            if (!writesFirst) writes = BackupTestSupport.WriteConcurrentAsync(catalog);
            reached.Set();
            Assert.True(resume.Wait(TestWaits.Condition));
        };
        var backup = service.BackupAsync();
        try
        {
            Assert.True(reached.Wait(TestWaits.Condition));
            Assert.Same(backup, service.BackupIfDueAsync());
            if (!writesFirst) Assert.False(writes.IsCompleted);
            await presets.SaveUserPresetAsync("Concurrent", new EditSettings { Exposure = 2 }, preset.Id);
        }
        finally { resume.Set(); }
        await backup.WaitAsync(TestWaits.Condition);
        await writes.WaitAsync(TestWaits.Condition);
        using var restore = new TemporaryDirectory();
        var item = Assert.Single(service.List());
        await BackupTestSupport.AssertArchiveAsync(item.Path + ".zip", restore.Path);
        BackupTestSupport.EqualRows(expected, Path.Combine(restore.Path, "catalog.db"));
    }

    [Fact]
    public async Task PresetReadAcrossRepeatedAtomicReplacementsRetainsWholeCommittedVersion()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var presets = new PresetService(Path.Combine(directory.Path, "presets"));
        await presets.InitializeAsync();
        var preset = await presets.SaveUserPresetAsync("Concurrent", new EditSettings { Exposure = 1 });
        var service = new CatalogBackupService(catalog);
        service.Step = step =>
        {
            if (step != "preset-open") return;
            for (var i = 0; i < 20; i++)
                presets.SaveUserPresetAsync("Concurrent", new EditSettings { Exposure = 1 + i % 2 }, preset.Id)
                    .GetAwaiter().GetResult();
        };
        await service.BackupAsync();
        using var restore = new TemporaryDirectory();
        var outcome = await Outcome(catalog);
        Assert.True(outcome.Status == "ok", outcome.Reason);
        await BackupTestSupport.AssertArchiveAsync(Assert.Single(service.List()).Path + ".zip", restore.Path);
        var restored = new PresetService(Path.Combine(restore.Path, "presets"));
        await restored.InitializeAsync();
        Assert.Equal(1, Assert.Single(restored.UserPresets).Settings.Exposure);
        Assert.Equal(2, presets.GetById(preset.Id)!.Settings.Exposure);
    }

    private static async Task<BackupOutcome> Outcome(CatalogService catalog) =>
        JsonSerializer.Deserialize<BackupOutcome>((await catalog.GetAppSettingAsync(CatalogBackupService.OutcomeKey))!)!;
}

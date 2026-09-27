using System.IO.Compression;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RestoreRecoveryReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupLockedFile_RetriesAfterRelease(bool removeMarker)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Requires Windows delete sharing."); return; }
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup);
        var state = (await journal.ReadJournalAsync()).Restore!;
        var staging = Path.Combine(f.Locations.CatalogRoot, ".restore-" + state.BeforeStem);
        FileStream? locked = null;
        journal.RestoreStep = step =>
        {
            if (step != "after:commit:NoticeRecorded") return;
            if (removeMarker) File.Delete(Path.Combine(staging, AppDataRootOwnership.MarkerFileName));
            locked = new FileStream(Path.Combine(staging, "preservation", "catalog.db"),
                FileMode.Open, FileAccess.Read, FileShare.Read);
        };
        try { await Assert.ThrowsAnyAsync<IOException>(journal.ExecutePendingAsync); }
        finally { locked?.Dispose(); }
        Assert.True(File.Exists(journal.JournalPath));
        if (!removeMarker) AppDataRootOwnership.AssertAppOwned(staging);
        await new CatalogLocationMigrator(f.Service).ExecutePendingAsync();
        Assert.False(Directory.Exists(staging));
        Assert.False(File.Exists(journal.JournalPath));
        Assert.Equal(RestoreTestSupport.Payload(f.Backup), RestoreTestSupport.Generation(f.Locations.CatalogRoot));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaseOnlyPresetName_RestoresExactCompleteGeneration(bool sameBytes)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var preset = Path.Combine(f.Locations.PresetsRoot, "first.json");
        if (sameBytes)
        {
            using var zip = ZipFile.OpenRead(f.Backup);
            zip.GetEntry("presets/first.json")!.ExtractToFile(preset, overwrite: true);
        }
        File.Move(preset, Path.Combine(f.Locations.PresetsRoot, "FIRST.json"));
        var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup);
        await journal.ExecutePendingAsync();
        Assert.Equal(RestoreTestSupport.Payload(f.Backup), RestoreTestSupport.Generation(f.Locations.CatalogRoot));
        RestoreTestSupport.AssertPreserved(f, original, damaged: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparedOwnershipRefusal_RemovesJournalWithoutChangingCatalog(bool cache)
    {
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path);
        var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        var journal = new CatalogLocationMigrator(f.Service);
        await new CatalogRestoreExecutor(journal).StageAsync(f.Locations, f.Backup);
        var root = cache ? f.Locations.CacheRoot : f.Locations.CatalogRoot;
        var marker = Path.Combine(root, AppDataRootOwnership.MarkerFileName);
        var contents = File.ReadAllText(marker);
        File.Delete(marker);
        await Assert.ThrowsAsync<AppDataOwnershipException>(journal.ExecutePendingAsync);
        Assert.False(File.Exists(journal.JournalPath));
        Assert.False(File.Exists(marker));
        File.WriteAllText(marker, contents);
        Assert.Equal(original, RestoreTestSupport.Generation(f.Locations.CatalogRoot));
    }
}

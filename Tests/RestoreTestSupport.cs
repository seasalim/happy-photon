using System.IO.Compression;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed record RestoreFixture(AppDataLocationService Service, AppDataLocations Locations, string Backup);

internal static class RestoreTestSupport
{
    internal static AppDataLocationService Service(string root) => new(new AppDataPlatformPaths(
        Path.Combine(root, "pictures"), Path.Combine(root, "pointer"),
        Path.Combine(root, "catalog"), Path.Combine(root, "cache")));

    internal static async Task<RestoreFixture> CreateAsync(string root, BackupCatalogFixture? fixture = null)
    {
        var service = Service(root);
        var locations = await service.CreateFreshAsync(useStandardCatalog: true);
        Directory.CreateDirectory(Path.Combine(root, "pictures"));
        if (fixture != null)
        {
            File.Copy(fixture.DatabasePath, locations.DatabasePath);
            File.Copy(Path.Combine(fixture.Root, ".catalog-identity"), Path.Combine(locations.CatalogRoot, ".catalog-identity"));
        }
        using var catalog = new CatalogService();
        await catalog.InitializeAsync(locations);
        await catalog.SetAppSettingAsync("FirstRunExperienceVersion", "1");
        await catalog.SetAppSettingAsync("RootFolderPath", Path.Combine(root, "pictures"));
        await catalog.GetOrCreateImageAsync(Path.Combine(root, "original.jpg"));
        var presets = new PresetService(locations.PresetsRoot);
        await presets.InitializeAsync();
        await presets.SaveUserPresetAsync("First", new EditSettings { Exposure = 1 });
        var presetPath = Assert.Single(Directory.GetFiles(locations.PresetsRoot));
        File.Move(presetPath, Path.Combine(locations.PresetsRoot, "first.json"));
        var backup = new CatalogBackupService(catalog);
        await backup.BackupAsync();
        var path = Assert.Single(backup.List()).Path + ".zip";
        await catalog.GetOrCreateImageAsync(Path.Combine(root, "later.jpg"));
        File.WriteAllText(Path.Combine(locations.PresetsRoot, "first.json"), "changed preset");
        File.WriteAllText(Path.Combine(locations.PresetsRoot, "extra.json"), "extra preset");
        foreach (var name in new[] { "thumbs", "previews", "rendered-thumbs", "tmp" })
            File.WriteAllText(Path.Combine(locations.AssetsRoot, name, "old"), "stale");
        return new(service, locations, path);
    }

    internal static SortedDictionary<string, string> Generation(string root) => new(
        CatalogRestorePreservation.Files(root).ToDictionary(name => name,
            name => CatalogBackupService.Hash(Path.Combine(root, name))), StringComparer.Ordinal);

    internal static SortedDictionary<string, string> Payload(string path) => new(
        CatalogBackupService.VerifyArchive(path).Entries.ToDictionary(e => e.Key, e => e.Value.Sha256), StringComparer.Ordinal);

    internal static void AssertPreserved(RestoreFixture fixture, SortedDictionary<string, string> original, bool damaged)
    {
        using var catalog = new CatalogService(fixture.Locations.CatalogRoot);
        var folder = new CatalogBackupService(catalog).Folder;
        var archives = Directory.GetFiles(folder, "*.zip").Where(path => path != fixture.Backup).ToArray();
        var archive = Assert.Single(archives, path =>
            CatalogBackupService.VerifyArchive(path).Kind == "before-restore");
        var manifest = CatalogBackupService.VerifyArchive(archive);
        Assert.Equal(damaged, manifest.Damaged);
        Assert.Equal(original, Payload(archive));
    }

    internal static void ChangeManifest(string path, Func<BackupManifest, BackupManifest> change)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = zip.GetEntry("manifest.json")!;
        BackupManifest manifest;
        using (var reader = new StreamReader(entry.Open()))
            manifest = JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd())!;
        entry.Delete();
        var next = change(manifest);
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            writer.Write(JsonSerializer.Serialize(next));
        File.WriteAllText(Path.ChangeExtension(path, ".manifest.json"), JsonSerializer.Serialize(next));
    }
}

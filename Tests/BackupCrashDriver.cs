using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

internal static class BackupCrashDriver
{
    internal static async Task PrepareAsync(string root, BackupCatalogFixture fixture)
    {
        BackupTestSupport.CopyCatalog(fixture.Root, root);

        using (var seedCatalog = new CatalogService(root))
        {
            await seedCatalog.InitializeAsync();
            var seed = new CatalogBackupService(seedCatalog) { UtcNow = () => DateTimeOffset.UtcNow.AddDays(-20) };
            var presets = new PresetService(Path.Combine(root, "presets"));
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
    }

    internal static async Task<string[]> DiscoverAsync(BackupCatalogFixtures fixtures)
    {
        using var directory = new TemporaryDirectory();
        var fixture = await (Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") == "1"
            ? fixtures.Archive : fixtures.Everyday);
        await PrepareAsync(directory.Path, fixture);
        var points = new List<string>();
        await RunAsync(directory.Path, (point, _) => points.Add(point));

        return points.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static async Task RunAsync(string root, Action<string, string> checkpoint)
    {
        using var catalog = new CatalogService(root);
        await catalog.InitializeAsync();
        var presets = new PresetService(Path.Combine(root, "presets"));
        await presets.InitializeAsync();
        var preset = Assert.Single(presets.UserPresets);
        Task writes = Task.CompletedTask;
        var expectedHash = BackupTestSupport.Hash(Path.Combine(root, "catalog.db"));
        var service = new CatalogBackupService(catalog);
        service.Step = point =>
        {
            if (point == "before:snapshot")
            {
                Assert.Equal(expectedHash, BackupTestSupport.Hash(Path.Combine(root, "catalog.db")));
                writes = BackupTestSupport.WriteConcurrentAsync(catalog);
            }

            if (point == "after:snapshot")
                presets.SaveUserPresetAsync("Concurrent", new EditSettings { Exposure = 2 }, preset.Id).GetAwaiter().GetResult();

            if (point is "preset-open" or "before:zip")
            {
                writes.GetAwaiter().GetResult();
                expectedHash = BackupTestSupport.Hash(Path.Combine(root, "catalog.db"));
            }

            if (point == "after:outcome") expectedHash = BackupTestSupport.Hash(Path.Combine(root, "catalog.db"));

            Signal(point);
        };
        presets.WriteStep = Signal;
        await service.BackupAsync();

        void Signal(string point) => checkpoint(point, expectedHash);
    }
}

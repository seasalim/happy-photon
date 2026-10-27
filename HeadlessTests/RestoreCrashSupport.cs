using System.IO.Compression;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

internal sealed class RestoreCrashSupport(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    internal const int BatchCount = 16;

    public static IEnumerable<object[]> OperationBatches() =>
        Enumerable.Range(0, BatchCount).Select(batch => new object[] { batch });

    internal async Task RunMatrix(bool corrupt, bool errorScreen, bool matchingPreset, int? batch = null)
    {
        var fixture = await fixtures.Everyday;
        using var probe = new TemporaryDirectory();
        var boundaries = await Discover(probe.Path, matchingPreset);
        var discovered = boundaries;
        var pathName = RestoreCrashEquivalence.PathName(corrupt, errorScreen, matchingPreset);

        if (!matchingPreset)
        {
            // The other theory covers shared operations; exercise this destructive branch separately.
            boundaries = boundaries.Where(point => point.Contains(":case-delete:")).ToArray();

            if (OperatingSystem.IsWindows()) Assert.Equal(2, boundaries.Length);

            using var matchingProbe = new TemporaryDirectory();
            var matchingBoundaries = await Discover(matchingProbe.Path, matchesPreset: true);
            RestoreCrashEquivalence.ValidateCaseOnlyCoverage(pathName, discovered, boundaries, matchingBoundaries);
        }

        var map = RestoreCrashEquivalence.Load(pathName);
        map?.Validate(pathName, boundaries, requireComplete: !RestoreCrashEquivalence.FullMatrix);

        if (map != null && !RestoreCrashEquivalence.FullMatrix)
        {
            boundaries = boundaries.Where(point => map.Kept.Contains(point, StringComparer.Ordinal)).ToArray();
        }

        if (batch.HasValue)
        {
            // Distribute the selected kill points across bounded cases.
            boundaries = boundaries.Order(StringComparer.Ordinal)
                .Where((_, index) => index % BatchCount == batch.Value).ToArray();
            Assert.NotEmpty(boundaries);
        }

        foreach (var point in boundaries)
        {
            using var directory = new TemporaryDirectory();
            var f = await Prepare(directory.Path, matchingPreset);
            var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
            var restored = RestoreTestSupport.Payload(f.Backup);
            var index = Array.IndexOf(discovered, point);
            var predecessor = index > 0 ? discovered[index - 1] : null;
            var equivalent = map?.Pruned.GetValueOrDefault(point);
            await RestoreCrashProcess.KillAsync(directory.Path, f.Backup, point, errorScreen,
                pathName, predecessor, equivalent, output);

            // P-1: committed replacement must not need the external archive anymore.
            var journal = new CatalogLocationMigrator(f.Service);
            var state = File.Exists(journal.JournalPath) ? (await journal.ReadJournalAsync()).Restore : null;
            if (state?.Phase >= CatalogRestorePhase.Replacing)
                File.Move(f.Backup, f.Backup + ".unavailable");

            using var catalog = new CatalogService();
            var vm = new MainWindowViewModel(catalog);
            var window = new MainWindow();
            using var scope = TestUiScope.ForMainWindow(window, vm);
            window.BeforeStartupSettingsLoad = () => throw new IOException("Keep recovery at startup for byte comparison.");

            try
            {
                await window.InitializeApplicationAsync(vm, catalog, f.Service, journal, Path.Combine(directory.Path, "pictures"))
                    .WaitAsync(TestWaits.Condition);
                catalog.Dispose();
                var actual = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
                var changed = !original.SequenceEqual(actual);
                Assert.Equal(changed ? restored : original, actual);
                if (changed) RestoreTestSupport.AssertPreserved(f, original, corrupt);
                using var listingCatalog = new CatalogService(f.Locations.CatalogRoot);
                var backupFolder = new CatalogBackupService(listingCatalog).Folder;
                Assert.Empty(Directory.GetFiles(backupFolder, "*.partial.*"));
                if (point == "after:journal-delete")
                    Assert.NotNull(CatalogRestoreExecutor.ReadNotice(f.Locations.CatalogRoot));
            }
            finally
            {
                scope.Dispose();
                await vm.DisposeAsync();
            }

            output.WriteLine($"G2 corrupt={corrupt} error_screen={errorScreen} {point}: complete bytes/identity/presets; preservation passed");
        }

        output.WriteLine($"G2: {boundaries.Length} real process terminations passed for this path/generation.");

        async Task<string[]> Discover(string root, bool matchesPreset)
        {
            var sample = await Prepare(root, matchesPreset);
            var points = new List<string>();
            var store = new CatalogLocationMigrator(sample.Service) { RestoreStep = points.Add };
            await new CatalogRestoreExecutor(store) { Step = points.Add }.StageAsync(sample.Locations, sample.Backup, true);
            await store.ExecutePendingAsync();
            var boundaries = points.Where(p => p.StartsWith("before:") || p.StartsWith("after:")).Distinct().ToArray();
            Assert.Contains("before:replace:catalog.db", boundaries);
            Assert.Contains("after:replace:.catalog-identity", boundaries);
            Assert.Contains("after:journal-delete", boundaries);

            return boundaries;
        }

        async Task<RestoreFixture> Prepare(string root, bool matchesPreset)
        {
            var f = await RestoreTestSupport.CreateAsync(root, fixture);

            if (matchesPreset)
            {
                using var zip = ZipFile.OpenRead(f.Backup);
                zip.GetEntry("presets/first.json")!.ExtractToFile(
                    Path.Combine(f.Locations.PresetsRoot, "first.json"), overwrite: true);
            }

            File.Move(Path.Combine(f.Locations.PresetsRoot, "first.json"),
                Path.Combine(f.Locations.PresetsRoot, "FIRST.json"));
            if (corrupt) File.WriteAllText(f.Locations.DatabasePath, "genuinely corrupt database");
            File.WriteAllText(Path.Combine(f.Locations.CatalogRoot, ".catalog-identity"),
                $$"""{"version":1,"catalogId":"{{Guid.NewGuid()}}"}""");

            return f;
        }
    }
}

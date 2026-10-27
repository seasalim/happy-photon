using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class MoveBackupCrashDriver
{
    internal static async Task<string[]> DiscoverAsync(BackupCatalogFixtures fixtures)
    {
        using var directory = new TemporaryDirectory();
        var sample = await MoveBackupTestSupport.CreateAsync(directory.Path, await fixtures.Archive);
        await new CatalogLocationMigrator(sample.Service).StageMoveAsync(
            sample.Locations, CatalogLocationMoveKind.Catalog, sample.Destination);
        var points = new List<string>();
        await RunAsync(directory.Path, point => points.Add(point));

        return points.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static Task RunAsync(string root, Action<string> checkpoint) =>
        new CatalogLocationMigrator(RestoreTestSupport.Service(root), phase => checkpoint("phase:" + phase))
        {
            BackupMoveStep = checkpoint
        }.ExecutePendingAsync();
}

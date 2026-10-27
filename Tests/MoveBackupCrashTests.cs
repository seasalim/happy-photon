using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class MoveBackupCrashTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    public static IEnumerable<object[]> Points() =>
        BackupKillPointMatrix.Move.Rows(part: 0, partCount: BackupKillPointMatrix.MovePartCount);

    [Theory]
    [MemberData(nameof(Points))]
    public async Task KilledMover_PreservesSourceBeforeFlip_RecoversVerifiedDestination(string point)
    {
        var matrix = BackupKillPointMatrix.Move;
        var discovered = await matrix.DiscoverAsync(fixtures);

        foreach (var target in matrix.Targets(point, discovered))
        {
            await KillAtAsync(target, discovered);
        }
    }

    private async Task KillAtAsync(string point, string[] discovered)
    {
        using var directory = new TemporaryDirectory();
        var f = await MoveBackupTestSupport.CreateAsync(directory.Path, await fixtures.Archive);
        var expected = MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot);
        var original = RestoreTestSupport.Generation(f.Locations.CatalogRoot);
        var store = new CatalogLocationMigrator(f.Service);
        await store.StageMoveAsync(f.Locations, CatalogLocationMoveKind.Catalog, f.Destination);
        using var child = new BackupKillPointProcess(BackupKillPointMatrix.Move,
            directory.Path, point, discovered, output);
        await child.KillAsync();

        Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot));
        Assert.Equal(original, RestoreTestSupport.Generation(f.Locations.CatalogRoot));

        if (point == "phase:PointerFlipped")
        {
            Assert.Equal(f.Destination, (await f.Service.ResolveAsync())!.CatalogRoot);
            Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Destination));
        }
        else
        {
            Assert.Equal(f.Locations.CatalogRoot, (await f.Service.ResolveAsync())!.CatalogRoot);
        }

        await new CatalogLocationMigrator(f.Service).ExecutePendingAsync();
        Assert.Equal(f.Destination, (await f.Service.ResolveAsync())!.CatalogRoot);
        Assert.Equal(expected, MoveBackupTestSupport.Hashes(f.Destination));
        Assert.Empty(MoveBackupTestSupport.Hashes(f.Locations.CatalogRoot));
        Assert.False(File.Exists(store.JournalPath));
        output.WriteLine($"G3 {point}: terminated child; preserved source and verified destination; recovery passed");
    }
}

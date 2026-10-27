using Xunit;

namespace HappyPhoton.Tests;

public sealed class MoveBackupCrashPart2Tests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    public static IEnumerable<object[]> Points() =>
        BackupKillPointMatrix.Move.Rows(part: 1, partCount: BackupKillPointMatrix.MovePartCount);

    [Theory]
    [MemberData(nameof(Points))]
    public Task KilledMover_PreservesSourceBeforeFlip_RecoversVerifiedDestination(string point) =>
        new MoveBackupCrashTests(fixtures, output)
            .KilledMover_PreservesSourceBeforeFlip_RecoversVerifiedDestination(point);
}

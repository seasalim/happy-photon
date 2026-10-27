using Xunit;

namespace HappyPhoton.Tests;

public sealed class MoveBackupCrashPart3Tests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    public static IEnumerable<object[]> Points() =>
        BackupKillPointMatrix.Move.Rows(part: 2, partCount: BackupKillPointMatrix.MovePartCount);

    [Theory]
    [MemberData(nameof(Points))]
    public Task KilledMover_PreservesSourceBeforeFlip_RecoversVerifiedDestination(string point) =>
        new MoveBackupCrashTests(fixtures, output)
            .KilledMover_PreservesSourceBeforeFlip_RecoversVerifiedDestination(point);
}

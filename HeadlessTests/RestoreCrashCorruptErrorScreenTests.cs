using Avalonia.Headless.XUnit;
using Xunit;

namespace HappyPhoton.Tests;

[Trait("ShardCost", "144.0")]
public sealed class RestoreCrashCorruptErrorScreenTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    [AvaloniaTheory]
    [MemberData(nameof(RestoreCrashSupport.OperationBatches), MemberType = typeof(RestoreCrashSupport))]
    public Task G2_KillEveryOperation_RecoversACompleteGeneration(int batch) =>
        new RestoreCrashSupport(fixtures, output).RunMatrix(true, true, matchingPreset: true, batch);

    [AvaloniaFact]
    public Task G2_KillCaseOnlyReplacementOfDifferentBytes_RecoversACompleteGeneration() =>
        new RestoreCrashSupport(fixtures, output).RunMatrix(true, true, matchingPreset: false);
}

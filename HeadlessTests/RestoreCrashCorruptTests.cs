using Avalonia.Headless.XUnit;
using Xunit;

namespace HappyPhoton.Tests;

[Trait("ShardCost", "45.3")]
public sealed class RestoreCrashCorruptTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    [AvaloniaTheory]
    [MemberData(nameof(RestoreCrashSupport.OperationBatches), MemberType = typeof(RestoreCrashSupport))]
    public Task G2_KillEveryOperation_RecoversACompleteGeneration(int batch) =>
        new RestoreCrashSupport(fixtures, output).RunMatrix(true, false, matchingPreset: true, batch);

    [AvaloniaFact]
    public Task G2_KillCaseOnlyReplacementOfDifferentBytes_RecoversACompleteGeneration() =>
        new RestoreCrashSupport(fixtures, output).RunMatrix(true, false, matchingPreset: false);
}

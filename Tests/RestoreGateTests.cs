using System.Diagnostics;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(BackupBaselineCollection.Name)]
public sealed class RestoreGateTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    [Fact]
    public async Task G1_ArchiveFiveRestoresBeforeOpen()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Run under the exclusive host measure lock with HAPPY_PHOTON_PERF=1.");
        await new BackupBaselineTests(fixtures, output).CopyControl_ReportsPinnedSizesAndFiveSamples();
        var fixture = await fixtures.Archive;
        Assert.InRange(fixture.SizeMiB, 160, 260);
        using var directory = new TemporaryDirectory();
        var f = await RestoreTestSupport.CreateAsync(directory.Path, fixture);
        var store = new CatalogLocationMigrator(f.Service);
        var samples = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            await new CatalogRestoreExecutor(store).StageAsync(f.Locations, f.Backup);
            var clock = Stopwatch.StartNew();
            await store.ExecutePendingAsync();
            samples.Add(clock.Elapsed.TotalMilliseconds);
            output.WriteLine($"G1 restore {i + 1}: {samples[^1]:F1} ms");
        }
        var median = samples.Order().ElementAt(2);
        output.WriteLine($"G1 median: {median:F1} ms; archive {fixture.SizeMiB:F2} MiB; limit 8000 ms");
        Assert.InRange(median, 0, 8000);
    }
}

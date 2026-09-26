using System.Diagnostics;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(BackupBaselineCollection.Name)]
public sealed class BackupGateTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    [Fact]
    public async Task FiveRunEnvelope()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Run under the exclusive measure lock with HAPPY_PHOTON_PERF=1.");
        await new BackupBaselineTests(fixtures, output).CopyControl_ReportsPinnedSizesAndFiveSamples();
        await Measure(await fixtures.Archive, 160, 260, 6000, gateLimit: 1500);
        await Measure(await fixtures.Everyday, 1.5, 4, 250, gateLimit: null);
    }

    private async Task Measure(BackupCatalogFixture fixture, double minimum, double maximum,
        double elapsedLimit, double? gateLimit)
    {
        Assert.InRange(fixture.SizeMiB, minimum, maximum);
        using var directory = new TemporaryDirectory();
        BackupTestSupport.CopyCatalog(fixture.Root, directory.Path);
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var service = new CatalogBackupService(catalog);
        var gate = new List<double>();
        var elapsed = new List<double>();
        catalog.BackupGateMeasured = gate.Add;
        for (var run = 0; run < 5; run++)
        {
            var timer = Stopwatch.StartNew();
            double promoted = 0;
            service.Step = step => { if (step == "after:publish-sidecar") promoted = timer.Elapsed.TotalMilliseconds; };
            await service.BackupAsync();
            Assert.True(promoted > 0, await catalog.GetAppSettingAsync(CatalogBackupService.OutcomeKey));
            elapsed.Add(elapsedLimit == 250 ? timer.Elapsed.TotalMilliseconds : promoted);
            output.WriteLine($"images={fixture.ImageRows} run={run + 1} gate_ms={gate[^1]:F3} elapsed_ms={elapsed[^1]:F3}");
        }
        output.WriteLine($"images={fixture.ImageRows} size_mib={fixture.SizeMiB:F3} max_gate_ms={gate.Max():F3} median_ms={elapsed.Order().ElementAt(2):F3}");
        if (gateLimit.HasValue) Assert.InRange(gate.Max(), 0, gateLimit.Value);
        Assert.InRange(elapsed.Order().ElementAt(2), 0, elapsedLimit);
    }
}

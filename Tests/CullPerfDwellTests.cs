using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CullPerfDwellTests
{
    [Fact]
    public void SupplementalTableKeepsFrozenMetricsAndDistinctColdCadence()
    {
        var frozen = CullPerfFiles.Read<CullPerfGateFile>(
            Path.Combine(CullPerfFiles.Root, "Tests", "CullPerfGates.json"));
        var dwell = CullPerfFiles.Read<CullPerfGateFile>(
            Path.Combine(CullPerfFiles.Root, "Tests", "CullPerfGates.dwell.json"));
        Assert.Equal(new[] { "loupe", "develop" }, dwell.Workloads.Select(item => item.Surface));
        Assert.Equal(2, dwell.Workloads.Select(item => item.Id).Distinct().Count());
        Assert.All(dwell.Metrics.Where(metric => metric.Id != "ui-enqueue-ms"), metric => Assert.Equal(
            frozen.Metrics.Single(item => item.Id == metric.Id), metric));
        Assert.Equal(new CullPerfMetric("ui-enqueue-ms", "p95", 1, null, false),
            dwell.Metrics.Single(metric => metric.Id == "ui-enqueue-ms"));
        Assert.All(dwell.Workloads, item =>
        {
            Assert.DoesNotContain(frozen.Workloads, original => original.Id == item.Id);
            Assert.Equal("dwell", item.Pattern);
            Assert.Equal("canon", item.Fixture);
            Assert.Equal("cold", item.CacheCondition);
            Assert.Equal("default", item.Settings);
            Assert.Equal(100, item.Actions);
            Assert.Equal(new double[] { 0, 1500, 3000, 4500 },
                Enumerable.Range(0, 4).Select(index => CullPerfPreparation.DueMilliseconds(item, index)));
        });
    }

    [Theory]
    [InlineData("FreshRender", 1, 19, true)]
    [InlineData("CachedJpeg", 1, 19, false)]
    [InlineData("FreshRender", 2, 19, false)]
    [InlineData("FreshRender", 1, 21, false)]
    public void RequiresFreshMatchingGenerationBeforeTheNextInput(
        string kind, long generation, long timestamp, bool valid)
    {
        CullPerfSubmission[] ledger = [new(1, 10, 2, false), new(2, 20, 3, false)];
        CullPerfEvent[] events =
        [
            new(1, "FreshRender", 0, 1, 0, 1, 0),
            new(11, "Selection", 1, 2, 1, 1, 0),
            new(timestamp, kind, 1, 2, generation, 1, 0),
            new(22, "Selection", 2, 3, 2, 1, 0),
            new(23, "FreshRender", 2, 3, 2, 1, 0)
        ];
        var failures = new List<string>();
        Assert.Equal(valid ? 2 : 1, CullPerfDwell.Validate(ledger, events, 1, failures));
        Assert.Equal(valid ? 0 : 1, failures.Count);
    }

    [Fact]
    public void RequiresAnInitialRenderedPreviewToDepart()
    {
        var failures = new List<string>();
        CullPerfDwell.Validate([new(1, 10, 2, false)], [], 1, failures);
        Assert.Contains(failures, failure => failure.Contains("initial image"));
    }
}

using Xunit;

namespace HappyPhoton.Tests;

public sealed class CullPerfGateDefinitionTests
{
    [Fact]
    public void FrozenTableCoversApplicableSurfacesPatternsFixturesAndCaches()
    {
        var gates = CullPerfFiles.Read<CullPerfGateFile>(CullPerfFiles.GatePath);
        Assert.Equal(gates.Workloads.Length, gates.Workloads.Select(item => item.Id).Distinct().Count());
        Assert.Equal(gates.Metrics.Length, gates.Metrics.Select(item => item.Id).Distinct().Count());
        foreach (var surface in new[] { "loupe", "develop", "fullscreen" })
        foreach (var fixture in new[] { "jpeg", "canon", "fuji" })
        {
            var cases = gates.Workloads.Where(item => item.Surface == surface && item.Fixture == fixture).ToArray();
            var patterns = surface == "fullscreen" ? new[] { "steady", "burst", "reversal" }
                : ["steady", "burst", "reversal", "picks", "ahead"];
            Assert.All(patterns, pattern => Assert.Contains(cases, item => item.Pattern == pattern));
            Assert.All(new[] { "cold", "thumbnail", "matched", "stale" },
                cache => Assert.Contains(cases, item => item.CacheCondition == cache));
            Assert.Contains(cases, item => item.Settings == "default");
            Assert.Contains(cases, item => item.Settings == "edited");
            Assert.All(cases, item => Assert.True(item.Actions >= 100));
        }
        Assert.All(gates.Workloads, item => Assert.All(item.Metrics,
            metric => Assert.Contains(gates.Metrics, definition => definition.Id == metric)));
        Assert.All(gates.FixtureHashes.Values, hash => Assert.Matches("^[0-9a-f]{64}$", hash));
    }

    [Fact]
    public void FrozenCadenceIsIndependentOfCompletion()
    {
        var workload = new CullPerfWorkload("burst", "loupe", "burst", "jpeg", "default",
            "cold", 100, [], IntervalMs: 250, BurstSize: 5, BurstStepMs: 10);
        Assert.Equal(new double[] { 0, 10, 20, 30, 40, 250 },
            Enumerable.Range(0, 6).Select(index => CullPerfPreparation.DueMilliseconds(workload, index)));
    }
}

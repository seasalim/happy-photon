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
        Assert.Equal(new[] { "loupe", "develop", "loupe", "develop" }, dwell.Workloads.Select(item => item.Surface));
        Assert.Equal(4, dwell.Workloads.Select(item => item.Id).Distinct().Count());
        Assert.All(dwell.Metrics.Where(metric => frozen.Metrics.Any(item => item.Id == metric.Id)), metric => Assert.Equal(
            frozen.Metrics.Single(item => item.Id == metric.Id), metric));
        Assert.Equal(new CullPerfMetric("ui-enqueue-ms", "p95", 1, null, false),
            dwell.Metrics.Single(metric => metric.Id == "ui-enqueue-ms"));
        Assert.Equal(new CullPerfMetric("handoff-gap-ms", "p95", 20, 5, true),
            dwell.Metrics.Single(metric => metric.Id == "handoff-gap-ms"));
        Assert.Equal(new CullPerfMetric("step-refill-ms", "p95", 15, null, true, 1.1),
            dwell.Metrics.Single(metric => metric.Id == "step-refill-ms"));
        Assert.Equal(new CullPerfMetric("warm-save-ms", "p95", 80, null, true, 0.5),
            dwell.Metrics.Single(metric => metric.Id == "warm-save-ms"));
        Assert.Equal(new CullPerfMetric("initial-walk-ms", "p95", 1, null, false),
            dwell.Metrics.Single(metric => metric.Id == "initial-walk-ms"));
        Assert.All(dwell.Workloads, item =>
        {
            Assert.DoesNotContain(frozen.Workloads, original => original.Id == item.Id);
            Assert.Contains("handoff-gap-ms", item.Metrics);
            Assert.Contains("step-refill-ms", item.Metrics);
            Assert.Contains("initial-walk-ms", item.Metrics);
            var jump = item.Pattern == "jump";
            Assert.Contains(item.Pattern, new[] { "dwell", "jump" });
            Assert.Equal(jump ? 15 : 0, item.MinimumWalks);
            // Report-only everywhere since the single-encode series: the write lands
            // before the next worker arrives, so a wait, and thus a sample, is rare.
            Assert.Equal(new CullPerfMetric("handoff-gap-ms", "p95", 1, null, false),
                item.Metric(dwell, "handoff-gap-ms"));
            Assert.Equal(new CullPerfMetric("step-refill-ms", "p95", jump ? 15 : 1, null, jump, jump ? 1.1 : null),
                item.Metric(dwell, "step-refill-ms"));
            Assert.Equal(new CullPerfMetric("warm-save-ms", "p95", jump ? 80 : 1, null, jump, jump ? 0.5 : null),
                item.Metric(dwell, "warm-save-ms"));
            Assert.Equal(jump ? 20 : 100, item.Metric(dwell, "input-feedback-ms").MinimumSamples);
            Assert.Equal("canon", item.Fixture);
            Assert.Equal("cold", item.CacheCondition);
            Assert.Equal("default", item.Settings);
            Assert.Equal(jump ? 20 : 100, item.Actions);
            Assert.Equal(Enumerable.Range(0, 4).Select(index => index * (jump ? 9000d : 1500d)),
                Enumerable.Range(0, 4).Select(index => CullPerfPreparation.DueMilliseconds(item, index)));
        });
    }

    [Fact]
    public void JumpTargetsSixPositionsAheadAndReconcilesAsNavigation()
    {
        var images = Enumerable.Range(0, 130)
            .Select(index => new HappyPhoton.Models.ImageFile($"image-{index}.cr2") { CatalogId = index + 1 }).ToArray();
        Assert.Same(images[6], CullPerfPreparation.JumpTarget(images, images[0]));
        Assert.Same(images[12], CullPerfPreparation.JumpTarget(images, images[6]));
        Assert.Same(images[120], CullPerfPreparation.JumpTarget(images, images[114]));
        CullPerfSubmission[] ledger = [new(1, 10, images[6].CatalogId, false, DirectSelection: true)];
        CullPerfEvent[] events =
        [

            new(12, "Selection", 0, 7, 1, 1, 0),
            new(13, "SelectionFeedback", 0, 7, 1, 1, 0),
            new(14, "FreshRender", 0, 7, 1, 1, 0)
        ];
        var accounting = CullPerfLedger.Reconcile(ledger, events);
        Assert.Equal(1, accounting.Completed);
        Assert.Equal(0, accounting.NoOp);
        Assert.Empty(accounting.Failures);
        Assert.Single(accounting.Samples["input-feedback-ms"]);
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

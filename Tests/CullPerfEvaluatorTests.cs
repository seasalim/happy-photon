using Xunit;

namespace HappyPhoton.Tests;

public sealed class CullPerfEvaluatorTests
{
    [Fact]
    public void Statistics_KeepSlowSamplesAndUseNearestRankP95()
    {
        var samples = Enumerable.Range(1, 100)
            .Select(value => new CullPerfSample(value, value)).ToArray();
        var statistics = CullPerfStatistics.From(samples);
        Assert.Equal(100, statistics.Count);
        Assert.Equal(50.5, statistics.Median);
        Assert.Equal(95, statistics.P95);
        Assert.Equal(100, statistics.Maximum);
    }

    [Fact]
    public void CompleteEvidence_PassesAndPreservesIndividualSamples()
    {
        var fragment = Fragment();
        var result = Evaluate(fragment);
        Assert.Equal(CullPerfVerdict.Pass, result.Verdict);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(100, Assert.Single(result.Gates).Statistics!.Count);
        Assert.Equal(100, fragment.Samples["feedback"].Length);
    }

    [Fact]
    public void SlowEvidence_FailsWithoutDiscardingTheAttempt()
    {
        var result = Evaluate(Fragment(51));
        Assert.Equal(CullPerfVerdict.Fail, result.Verdict);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(51, Assert.Single(result.Gates).After);
    }

    [Fact]
    public void MissingOrDuplicateWorkload_IsInconclusive()
    {
        AssertInconclusive(CullPerfEvaluator.Evaluate(Gates(), "gate-hash", []));
        AssertInconclusive(CullPerfEvaluator.Evaluate(Gates(), "gate-hash", [Fragment(), Fragment()]));
    }

    [Theory]
    [InlineData("missing-events")]
    [InlineData("skipped")]
    [InlineData("not-executed")]
    [InlineData("lost-events")]
    [InlineData("gate-hash")]
    [InlineData("fixture-hash")]
    [InlineData("cache-condition")]
    [InlineData("machine")]
    [InlineData("short-ledger")]
    [InlineData("unreconciled")]
    [InlineData("no-op")]
    [InlineData("negative-count")]
    public void IncompleteEvidence_CannotPass(string defect)
    {
        var fragment = Fragment();
        fragment = defect switch
        {
            "missing-events" => fragment with { NotMeasured = ["feedback"] },
            "skipped" => fragment with { Skipped = true },
            "not-executed" => fragment with { Executed = false },
            "lost-events" => fragment with { LostEvents = 1 },
            "gate-hash" => fragment with { GateHash = "different" },
            "fixture-hash" => fragment with { FixtureHashes = new() { ["jpeg"] = "different" } },
            "cache-condition" => fragment with { CacheCondition = "cold" },
            "machine" => fragment with { MachineIdentity = "" },
            "short-ledger" => fragment with { Submitted = 99, Completed = 99 },
            "unreconciled" => fragment with { Completed = 99 },
            "no-op" => fragment with { Completed = 99, NoOp = 1 },
            "negative-count" => fragment with { Completed = 101, Cancelled = -1 },
            _ => throw new ArgumentException(defect)
        };
        AssertInconclusive(Evaluate(fragment));
    }

    [Fact]
    public void ShortMissingInvalidOrDuplicatedSamples_CannotPass()
    {
        var samples = Fragment().Samples["feedback"];
        foreach (var defective in new[]
        {
            samples[..99],
            samples.Select(sample => sample with { OperationId = 1 }).ToArray(),
            samples.Select(sample => sample with { Value = double.NaN }).ToArray(),
            samples.Select(sample => sample with { Value = double.PositiveInfinity }).ToArray(),
            samples.Select(sample => sample with { Value = -1 }).ToArray()
        })
        {
            AssertInconclusive(Evaluate(Fragment() with
            {
                Samples = new() { ["feedback"] = defective }
            }));
        }
        AssertInconclusive(Evaluate(Fragment() with { Samples = new() }));
    }

    [Fact]
    public void CorrectnessFailure_RejectsFastFeedbackEvenWithIdleWriter()
    {
        var result = Evaluate(Fragment(1) with
        {
            CorrectnessFailures = ["Sidecar failed; pending flag axis remains."],
            Counters = new() { ["sidecarWriterIdle"] = 1 }
        });
        Assert.Equal(CullPerfVerdict.Fail, result.Verdict);
        Assert.Contains(result.Gates, gate => gate.MetricId == "correctness");
    }

    [Fact]
    public void BaselineComparison_ReportsBeforeAfterAndTenPercentBoundary()
    {
        var pass = CullPerfEvaluator.Evaluate(Gates(), "gate-hash", [Fragment(44)], [Fragment(40)]);
        var regression = Assert.Single(pass.Gates, gate => gate.MetricId.EndsWith(".regression"));
        Assert.Equal(CullPerfVerdict.Pass, pass.Verdict);
        Assert.Equal(40, regression.Before);
        Assert.Equal(44, regression.After);
        var fail = CullPerfEvaluator.Evaluate(Gates(), "gate-hash", [Fragment(44.01)], [Fragment(40)]);
        Assert.Equal(CullPerfVerdict.Fail, fail.Verdict);
    }

    [Theory]
    [InlineData("machine")]
    [InlineData("gate")]
    [InlineData("fixture")]
    [InlineData("cache")]
    [InlineData("missing-events")]
    [InlineData("skipped")]
    public void BaselinePrerequisiteMismatch_IsInconclusive(string defect)
    {
        var baseline = Fragment();
        baseline = defect switch
        {
            "machine" => baseline with { MachineIdentity = "another-host" },
            "gate" => baseline with { GateHash = "another-gate" },
            "fixture" => baseline with { FixtureHashes = new() },
            "cache" => baseline with { CacheCondition = "cold" },
            "missing-events" => baseline with { NotMeasured = ["feedback"] },
            "skipped" => baseline with { Skipped = true },
            _ => throw new ArgumentException(defect)
        };
        AssertInconclusive(CullPerfEvaluator.Evaluate(Gates(), "gate-hash", [Fragment()], [baseline]));
    }

    [Fact]
    public void ReportOnlyMetric_StillRequiresEvidenceAndAppliesRegressionRule()
    {
        var gates = Gates();
        gates = gates with { Metrics = [gates.Metrics[0] with { Maximum = null }] };
        Assert.Equal(CullPerfVerdict.Pass,
            CullPerfEvaluator.Evaluate(gates, "gate-hash", [Fragment(1000)]).Verdict);
        Assert.Equal(CullPerfVerdict.Fail,
            CullPerfEvaluator.Evaluate(gates, "gate-hash", [Fragment(1000)], [Fragment(100)]).Verdict);
    }

    [Fact]
    public void NonForegroundComparisonRetainsBeforeAndAfterWithoutRegressionGate()
    {
        var gates = Gates();
        gates = gates with { Metrics = [gates.Metrics[0] with { Foreground = false, Maximum = null }] };
        var result = CullPerfEvaluator.Evaluate(gates, "gate-hash", [Fragment(80)], [Fragment(40)]);
        var comparison = Assert.Single(result.Gates, gate => gate.MetricId == "feedback.comparison");
        Assert.Equal(CullPerfVerdict.Pass, comparison.Verdict);
        Assert.Equal(40, comparison.Before);
        Assert.Equal(80, comparison.After);
    }

    [Fact]
    public void UnmeasuredReportOnlyMetricAndCoverageGapsDoNotBlockQualification()
    {
        var gates = Gates();
        gates = gates with
        {
            Metrics = [.. gates.Metrics, new("stage", "p95", 1, null, false)],
            Workloads = [gates.Workloads[0] with { Metrics = ["feedback", "stage"] }],
            KnownMissingEvidence = ["complete stage correlation"]
        };
        var fragment = Fragment() with { NotMeasured = ["stage"] };
        Assert.Equal(CullPerfVerdict.Pass,
            CullPerfEvaluator.Evaluate(gates, "gate-hash", [fragment], [fragment]).Verdict);
        var measured = Fragment() with
        {
            Samples = new(Fragment().Samples) { ["stage"] = [new(1, 20)] }
        };
        Assert.Equal(CullPerfVerdict.Pass,
            CullPerfEvaluator.Evaluate(gates, "gate-hash", [measured], [fragment]).Verdict);
        Assert.Equal(CullPerfVerdict.Fail,
            CullPerfEvaluator.Evaluate(gates, "gate-hash", [Fragment(51) with { NotMeasured = ["stage"] }]).Verdict);
        AssertInconclusive(CullPerfEvaluator.Evaluate(gates, "gate-hash", [Fragment()]));
        gates = gates with { Metrics = [gates.Metrics[0], gates.Metrics[1] with { Foreground = true }] };
        AssertInconclusive(CullPerfEvaluator.Evaluate(gates, "gate-hash", [fragment]));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"fragments\":null}")]
    [InlineData("{\"fragments\":[]}")]
    [InlineData("null")]
    [InlineData("invalid json")]
    public void NamedBaselineWithoutReadableFragments_IsInconclusive(string json)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "baseline.json");
        File.WriteAllText(path, json);
        var baseline = CullPerfFiles.ReadBaselineFragments(path);
        var result = CullPerfEvaluator.Evaluate(Gates(), "gate-hash", [Fragment()], baseline);
        AssertInconclusive(result);
        Assert.Contains(result.Gates, gate => gate.Reason == "Baseline evidence unreadable");
    }

    private static void AssertInconclusive(CullPerfEvaluation result)
    {
        Assert.Equal(CullPerfVerdict.Inconclusive, result.Verdict);
        Assert.Equal(2, result.ExitCode);
    }

    private static CullPerfEvaluation Evaluate(CullPerfFragment fragment) =>
        CullPerfEvaluator.Evaluate(Gates(), "gate-hash", [fragment]);

    private static CullPerfGateFile Gates() => new(
        1, "test-gates", 0.10, new() { ["jpeg"] = "fixture-hash" },
        [new("feedback", "p95", 100, 50, true)],
        [new("loupe-steady", "loupe", "steady", "jpeg", "default", "matched", 100, ["feedback"])]);

    private static CullPerfFragment Fragment(double value = 40) => new(
        "loupe-steady", "gate-hash", "test-host", new() { ["jpeg"] = "fixture-hash" },
        "matched", true, false, 0, 100, 100, 0, 0, 0, [],
        new() { ["feedback"] = Enumerable.Range(1, 100).Select(id => new CullPerfSample(id, value)).ToArray() },
        new());
}

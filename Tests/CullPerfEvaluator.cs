namespace HappyPhoton.Tests;

internal static class CullPerfEvaluator
{
    public static CullPerfEvaluation Evaluate(
        CullPerfGateFile gates,
        string gateHash,
        IReadOnlyList<CullPerfFragment> fragments,
        IReadOnlyList<CullPerfFragment>? baseline = null)
    {
        var results = new List<CullPerfGateResult>();
        if (baseline is { Count: 0 })
            results.Add(Inconclusive("baseline", "evidence", "Baseline evidence unreadable"));
        foreach (var workload in gates.Workloads)
        {
            var matches = fragments.Where(fragment => fragment.WorkloadId == workload.Id).ToArray();
            if (matches.Length != 1)
            {
                results.Add(Inconclusive(workload.Id, "evidence", "Expected exactly one workload fragment."));
                continue;
            }
            var fragment = matches[0];
            var invalid = ValidateEvidence(gates, gateHash, workload, fragment);
            if (invalid != null)
            {
                results.Add(Inconclusive(workload.Id, "evidence", invalid));
                continue;
            }
            foreach (var failure in fragment.CorrectnessFailures)
                results.Add(new(workload.Id, "correctness", CullPerfVerdict.Fail, failure));

            foreach (var metricId in workload.Metrics)
            {
                var metric = gates.Metrics.Single(metric => metric.Id == metricId);
                if (fragment.NotMeasured?.Contains(metric.Id) == true &&
                    metric.Maximum == null && !metric.Foreground) continue;
                var statistics = ReadStatistics(fragment, metric, out var reason);
                if (statistics == null)
                {
                    results.Add(Inconclusive(workload.Id, metric.Id, reason!));
                    continue;
                }
                var after = statistics.Select(metric.Statistic);
                var passed = metric.Maximum == null || after <= metric.Maximum;
                results.Add(new(workload.Id, metric.Id,
                    passed ? CullPerfVerdict.Pass : CullPerfVerdict.Fail,
                    metric.Maximum == null ? "Report only." : passed ? "Within threshold." : "Threshold exceeded.",
                    statistics, metric.Maximum, After: after));
                if (baseline != null)
                    results.Add(Compare(gates, gateHash, workload, metric, fragment, baseline, after));
            }
        }
        if (gates.Workloads.Length == 0 || gates.Metrics.Length == 0)
            results.Add(Inconclusive("configuration", "evidence", "No required workloads or metrics."));
        if (fragments.Any(fragment => !gates.Workloads.Any(workload => workload.Id == fragment.WorkloadId)))
            results.Add(Inconclusive("configuration", "evidence", "Unexpected workload fragment."));
        // Missing evidence can never qualify a run, even when another gate failed.
        var verdict = results.Any(result => result.Verdict == CullPerfVerdict.Inconclusive)
            ? CullPerfVerdict.Inconclusive
            : results.Any(result => result.Verdict == CullPerfVerdict.Fail)
                ? CullPerfVerdict.Fail : CullPerfVerdict.Pass;
        return new(verdict, results.ToArray());
    }

    private static string? ValidateEvidence(
        CullPerfGateFile gates,
        string gateHash,
        CullPerfWorkload workload,
        CullPerfFragment fragment)
    {
        if (!fragment.Executed || fragment.Skipped) return "Required workload did not execute.";
        if (fragment.GateHash != gateHash) return "Gate-file hash mismatch.";
        if (!EqualHashes(gates.FixtureHashes, fragment.FixtureHashes)) return "Fixture hash mismatch.";
        if (string.IsNullOrWhiteSpace(fragment.MachineIdentity)) return "Missing machine identity.";
        if (fragment.CacheCondition != workload.CacheCondition) return "Cache condition mismatch.";
        if (fragment.LostEvents != 0) return "Required events were lost.";
        if (fragment.Submitted != workload.Actions) return "Incorrect submitted action count.";
        if (fragment.Completed < 0 || fragment.Cancelled < 0 || fragment.Superseded < 0 || fragment.NoOp < 0)
            return "Invalid operation counts.";
        if ((long)fragment.Completed + fragment.Cancelled + fragment.Superseded + fragment.NoOp != fragment.Submitted)
            return "Operation ledger does not reconcile.";
        if (fragment.NoOp != 0) return "Guarded no-op cannot qualify as an action.";
        return null;
    }

    private static CullPerfStatistics? ReadStatistics(
        CullPerfFragment fragment,
        CullPerfMetric metric,
        out string? reason)
    {
        reason = null;
        if (fragment.NotMeasured?.Contains(metric.Id) == true)
        {
            reason = "Required metric was not measured.";
            return null;
        }
        if (!fragment.Samples.TryGetValue(metric.Id, out var samples) || samples.Length < metric.MinimumSamples)
            reason = "Insufficient samples.";
        else if (samples.Any(sample => !double.IsFinite(sample.Value) || sample.Value < 0 || sample.OperationId <= 0))
            reason = "Invalid sample.";
        else if (samples.Select(sample => sample.OperationId).Distinct().Count() != samples.Length)
            reason = "Duplicate operation samples.";
        return reason == null ? CullPerfStatistics.From(samples!) : null;
    }

    private static CullPerfGateResult Compare(
        CullPerfGateFile gates,
        string gateHash,
        CullPerfWorkload workload,
        CullPerfMetric metric,
        CullPerfFragment candidate,
        IReadOnlyList<CullPerfFragment> baseline,
        double after)
    {
        var id = metric.Id + (metric.Foreground ? ".regression" : ".comparison");
        var matches = baseline.Where(fragment => fragment.WorkloadId == workload.Id).ToArray();
        if (matches.Length != 1)
            return Inconclusive(workload.Id, id, "Expected exactly one named-baseline workload.");
        var beforeFragment = matches[0];
        var invalid = ValidateEvidence(gates, gateHash, workload, beforeFragment);
        if (invalid != null) return Inconclusive(workload.Id, id, "Baseline: " + invalid);
        if (beforeFragment.MachineIdentity != candidate.MachineIdentity)
            return Inconclusive(workload.Id, id, "Baseline machine identity mismatch.");
        if (beforeFragment.CorrectnessFailures.Length != 0)
            return Inconclusive(workload.Id, id, "Baseline has correctness failures.");
        if (beforeFragment.NotMeasured?.Contains(metric.Id) == true && metric.Maximum == null && !metric.Foreground)
            return new(workload.Id, id, CullPerfVerdict.Pass, "Report-only baseline metric was not measured.", After: after);
        var statistics = ReadStatistics(beforeFragment, metric, out var reason);
        if (statistics == null) return Inconclusive(workload.Id, id, "Baseline: " + reason);
        var before = statistics.Select(metric.Statistic);
        if (!metric.Foreground)
            return new(workload.Id, id, CullPerfVerdict.Pass, "Report-only baseline comparison.", Before: before, After: after);
        var maximum = before * (1 + gates.MaximumForegroundRegression);
        return new(workload.Id, id, after <= maximum ? CullPerfVerdict.Pass : CullPerfVerdict.Fail,
            after <= maximum ? "Within foreground regression allowance." : "Foreground regression exceeded.",
            Threshold: maximum, Before: before, After: after);
    }

    private static bool EqualHashes(Dictionary<string, string> expected, Dictionary<string, string> actual) =>
        expected.Count == actual.Count && expected.All(pair =>
            actual.TryGetValue(pair.Key, out var hash) && string.Equals(pair.Value, hash, StringComparison.Ordinal));

    private static CullPerfGateResult Inconclusive(string workload, string metric, string reason) =>
        new(workload, metric, CullPerfVerdict.Inconclusive, reason);
}

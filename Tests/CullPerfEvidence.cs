namespace HappyPhoton.Tests;

internal enum CullPerfVerdict { Pass, Fail, Inconclusive }

internal sealed record CullPerfMetric(
    string Id,
    string Statistic,
    int MinimumSamples,
    double? Maximum,
    bool Foreground,
    double? MaximumBaselineRatio = null);

internal sealed record CullPerfWorkload(
    string Id,
    string Surface,
    string Pattern,
    string Fixture,
    string Settings,
    string CacheCondition,
    int Actions,
    string[] Metrics,
    double IntervalMs = 150,
    int BurstSize = 1,
    double BurstStepMs = 0,
    int ReversalLength = 5,
    double Exposure = 0,
    double StaleExposureDelta = 0.5,
    int MinimumWalks = 0,
    CullPerfMetric[]? MetricOverrides = null)
{
    internal CullPerfMetric Metric(CullPerfGateFile gates, string id) =>
        MetricOverrides?.SingleOrDefault(metric => metric.Id == id) ??
        gates.Metrics.Single(metric => metric.Id == id);
}

internal sealed record CullPerfGateFile(
    int Version,
    string Id,
    double MaximumForegroundRegression,
    Dictionary<string, string> FixtureHashes,
    CullPerfMetric[] Metrics,
    CullPerfWorkload[] Workloads,
    string[]? KnownMissingEvidence = null,
    double RegressionFloorMs = 0,
    int RegressionMinimumSamples = 1);

internal sealed record CullPerfSample(long OperationId, double Value);

internal sealed record CullPerfFragment(
    string WorkloadId,
    string GateHash,
    string MachineIdentity,
    Dictionary<string, string> FixtureHashes,
    string CacheCondition,
    bool Executed,
    bool Skipped,
    long LostEvents,
    int Submitted,
    int Completed,
    int Cancelled,
    int Superseded,
    int NoOp,
    string[] CorrectnessFailures,
    Dictionary<string, CullPerfSample[]> Samples,
    Dictionary<string, double> Counters,
    string[]? NotMeasured = null);

internal sealed record CullPerfStatistics(int Count, double Median, double P95, double Maximum)
{
    public static CullPerfStatistics From(IEnumerable<CullPerfSample> samples)
    {
        var values = samples.Select(sample => sample.Value).Order().ToArray();
        if (values.Length == 0 || values.Any(value => !double.IsFinite(value) || value < 0))
            throw new ArgumentException("Samples must be nonempty, finite and nonnegative.");
        var middle = values.Length / 2;
        var median = values.Length % 2 == 0
            ? values[middle - 1] / 2 + values[middle] / 2
            : values[middle];
        return new(values.Length, median,
            values[(int)Math.Ceiling(values.Length * 0.95) - 1], values[^1]);
    }

    public double Select(string statistic) => statistic switch
    {
        "median" => Median,
        "p95" => P95,
        "max" => Maximum,
        _ => throw new ArgumentException($"Unknown statistic: {statistic}")
    };
}

internal sealed record CullPerfGateResult(
    string WorkloadId,
    string MetricId,
    CullPerfVerdict Verdict,
    string Reason,
    CullPerfStatistics? Statistics = null,
    double? Threshold = null,
    double? Before = null,
    double? After = null);

internal sealed record CullPerfEvaluation(CullPerfVerdict Verdict, CullPerfGateResult[] Gates)
{
    public int ExitCode => Verdict switch
    {
        CullPerfVerdict.Pass => 0,
        CullPerfVerdict.Fail => 1,
        _ => 2
    };
}

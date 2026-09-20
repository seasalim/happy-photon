namespace HappyPhoton.Tests;

internal static class CullPerfMigratedEvidence
{
    internal static void Write(string label,
        AdjacentPreviewPerformanceTests.AdjacentSample[] disabled,
        AdjacentPreviewPerformanceTests.AdjacentSample[] enabled)
    {
        if (Environment.GetEnvironmentVariable("CULL_PERF_RUN") == null) return;
        var id = "migrated-" + label.ToLowerInvariant();
        var directory = Path.Combine(CullPerfFiles.Run, id);
        Directory.CreateDirectory(directory);
        var samples = new Dictionary<string, CullPerfSample[]>();
        Add("warm-first-ms", sample => sample.FirstPaintMs);
        Add("priority-first-ms", sample => sample.PriorityPaintMs);
        Add(label == "JPEG" ? "jpeg-peak-bytes" : "raw-peak-bytes", sample => sample.WarmPeakDeltaBytes);
        Add(label == "JPEG" ? "jpeg-settled-bytes" : "raw-settled-bytes", sample => sample.WarmSettledDeltaBytes);
        // Preserve the approved ratio of medians, not the median of pairwise ratios.
        samples["warm-ratio"] = [new(1, Median(enabled.Select(x => x.FirstPaintMs)) / Median(disabled.Select(x => x.FirstPaintMs)))];
        samples["priority-ratio"] = [new(1, Median(enabled.Select(x => x.PriorityPaintMs)) / Median(disabled.Select(x => x.PriorityPaintMs)))];
        CullPerfFiles.WriteNew(Path.Combine(directory, "pairs.json"), new { disabled, enabled });
        CullPerfFiles.WriteNew(Path.Combine(directory, "fragment.json"), new CullPerfFragment(
            id, CullPerfFiles.Hash(CullPerfFiles.GatePath),
            Environment.GetEnvironmentVariable("CULL_PERF_MACHINE") ?? "",
            CullPerfFiles.Fixtures().Hashes, "paired-warm", true, false, enabled.Sum(sample => sample.LostEvents) + disabled.Sum(sample => sample.LostEvents),
            3, 3, 0, 0, 0, [], samples, []));

        void Add(string metric, Func<AdjacentPreviewPerformanceTests.AdjacentSample, double> selector) =>
            samples[metric] = enabled.Select((sample, index) => new CullPerfSample(index + 1, selector(sample))).ToArray();
    }

    private static double Median(IEnumerable<double> values) => values.Order().ElementAt(1);
}

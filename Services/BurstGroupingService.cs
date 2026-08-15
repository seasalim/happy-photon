namespace HappyPhoton.Services;

public record BurstGroup(
    string Id,
    IReadOnlyList<CaptureFileGroup> Captures,
    DateTime StartTime,
    double DurationSeconds)
{
    public IReadOnlyList<string> ImageIds { get; } = Captures
        .SelectMany(capture => capture.ImageIds)
        .ToList();
}

/// <summary>
/// Clusters images into bursts by capture time. Group ids are session-scoped and never persisted.
/// </summary>
public static class BurstGroupingService
{
    /// <summary>
    /// Clusters timestamped logical captures by consecutive capture-time gaps. Null timestamps
    /// are counted per file; retained groups and their members are ordered deterministically.
    /// </summary>
    public static (IReadOnlyList<BurstGroup> Groups, int ImagesWithoutTimestamp) ComputeGroups(
        IEnumerable<(string Id, DateTime? DateTaken)> images,
        double maxGapSeconds = 2.0,
        int minGroupSize = 2)
    {
        maxGapSeconds = Math.Max(0, maxGapSeconds);
        minGroupSize = Math.Max(1, minGroupSize);

        var files = images.ToList();
        var withoutTimestamp = files.Count(file => !file.DateTaken.HasValue);
        var timestampsById = files
            .GroupBy(file => file.Id, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Min(file => file.DateTaken),
                StringComparer.Ordinal);
        var stamped = CapturePairingService
            .GroupCaptures(files.Select(file => file.Id))
            .Select(capture => new
            {
                Capture = capture,
                Taken = capture.ImageIds
                    .Select(id => timestampsById[id])
                    .Min()
            })
            .Where(capture => capture.Taken.HasValue)
            .Select(capture => (
                capture.Capture,
                Taken: capture.Taken!.Value))
            .ToList();

        stamped.Sort((a, b) =>
        {
            var byTime = a.Taken.CompareTo(b.Taken);
            return byTime != 0
                ? byTime
                : string.CompareOrdinal(
                    a.Capture.ImageIds[0],
                    b.Capture.ImageIds[0]);
        });

        var groups = new List<BurstGroup>();
        var current = new List<(CaptureFileGroup Capture, DateTime Taken)>();

        void Flush()
        {
            if (current.Count >= minGroupSize)
            {
                groups.Add(new BurstGroup(
                    $"burst_{groups.Count + 1}",
                    current.Select(item => item.Capture).ToList(),
                    current[0].Taken,
                    (current[^1].Taken - current[0].Taken).TotalSeconds));
            }

            current.Clear();
        }

        foreach (var capture in stamped)
        {
            if (current.Count > 0 &&
                (capture.Taken - current[^1].Taken).TotalSeconds > maxGapSeconds)
            {
                Flush();
            }

            current.Add((capture.Capture, capture.Taken));
        }

        Flush();
        return (groups, withoutTimestamp);
    }
}

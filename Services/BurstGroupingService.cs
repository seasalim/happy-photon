namespace HappyPhoton.Services;

public record BurstGroup(
    string Id,
    IReadOnlyList<string> ImageIds,
    DateTime StartTime,
    double DurationSeconds);

/// <summary>
/// Clusters images into bursts by capture time. Group ids are session-scoped and never persisted.
/// </summary>
public static class BurstGroupingService
{
    /// <summary>
    /// Clusters timestamped images by consecutive capture-time gaps. Null timestamps are
    /// excluded and counted; retained groups and their members are ordered deterministically.
    /// </summary>
    public static (IReadOnlyList<BurstGroup> Groups, int ImagesWithoutTimestamp) ComputeGroups(
        IEnumerable<(string Id, DateTime? DateTaken)> images,
        double maxGapSeconds = 2.0,
        int minGroupSize = 2)
    {
        maxGapSeconds = Math.Max(0, maxGapSeconds);
        minGroupSize = Math.Max(1, minGroupSize);

        var stamped = new List<(string Id, DateTime Taken)>();
        var withoutTimestamp = 0;

        foreach (var (id, taken) in images)
        {
            if (taken.HasValue)
            {
                stamped.Add((id, taken.Value));
            }
            else
            {
                withoutTimestamp++;
            }
        }

        stamped.Sort((a, b) =>
        {
            var byTime = a.Taken.CompareTo(b.Taken);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Id, b.Id);
        });

        var groups = new List<BurstGroup>();
        var current = new List<(string Id, DateTime Taken)>();

        void Flush()
        {
            if (current.Count >= minGroupSize)
            {
                groups.Add(new BurstGroup(
                    $"burst_{groups.Count + 1}",
                    current.Select(frame => frame.Id).ToList(),
                    current[0].Taken,
                    (current[^1].Taken - current[0].Taken).TotalSeconds));
            }

            current.Clear();
        }

        foreach (var frame in stamped)
        {
            if (current.Count > 0 &&
                (frame.Taken - current[^1].Taken).TotalSeconds > maxGapSeconds)
            {
                Flush();
            }

            current.Add(frame);
        }

        Flush();
        return (groups, withoutTimestamp);
    }
}

using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class CullPerfCounters
{
    internal static Dictionary<string, double> Derive(CullPerfEvent[] events, long firstInput = long.MaxValue)
    {
        var result = events.GroupBy(item => item.Kind)
            .ToDictionary(group => "events-" + group.Key, group => (double)group.Count());
        var active = new Dictionary<int, CullPerfEvent>();
        var superseded = new HashSet<int>();
        var supersededMaximum = 0;
        var maximum = 0;
        var overlap = 0;
        foreach (var item in events)
        {
            if (item.Kind == "NativeStart")
            {
                if (active.Values.Any(other => other.ImageId == item.ImageId)) overlap++;
                superseded.Remove(item.WorkerId);
                active[item.WorkerId] = item;
                maximum = Math.Max(maximum, active.Count);
            }
            else if (item.Kind == "NativeEnd")
            {
                active.Remove(item.WorkerId);
                superseded.Remove(item.WorkerId);
            }
            else if (item.Kind is "Superseded" or "PaneSuperseded" or "WarmCancelRequested")
            {
                foreach (var call in active.Values.Where(call => call.ImageId == item.ImageId))
                    superseded.Add(call.WorkerId);
                supersededMaximum = Math.Max(supersededMaximum, superseded.Count);
            }
        }
        result["native-active-maximum"] = maximum;
        result["native-active-at-end"] = active.Count;
        result["overlapping-native-calls-same-image"] = overlap;
        result["duplicate-decodes"] = overlap;
        result["superseded-native-still-running-maximum"] = supersededMaximum;
        result["superseded-native-still-running-at-end"] = superseded.Count;
        result["published-bitmap-bytes-max"] = events.Where(CullPerfLedger.IsPaint)
            .Select(item => (double)item.Value).DefaultIfEmpty().Max();
        var walks = CullPerfLedger.Walks(events).ToArray();
        result["walks-without-complete"] = walks.Count(walk => walk.End == null);
        result["step-walks-completed"] = walks.Count(walk => walk.Start.Timestamp >= firstInput && walk.End != null);
        result["step-walks-excluded"] = walks.Count(walk => walk.Start.Timestamp >= firstInput && walk.End == null);
        // A worker that found the previous write already landed did not wait; the
        // gap metric samples only waits, so their count explains an empty metric.
        result["handoff-waits"] = events.Count(item => item.Kind == "WarmHandoffResolved" && item.Value == 1);
        return result;
    }
}

using System.Diagnostics;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal sealed record CullPerfSubmission(long Id, long Timestamp, long ImageId, bool Pick, bool DirectSelection = false);
internal sealed record CullPerfAccounting(int Completed, int Cancelled, int Superseded,
    int NoOp, string[] Failures, Dictionary<string, CullPerfSample[]> Samples,
    Dictionary<long, long> Operations);

internal static class CullPerfLedger
{
    internal static bool IsPaint(CullPerfEvent item) =>
        item.Kind is "FreshRender" or "CachedJpeg" or "RestingRender";

    internal static CullPerfAccounting Reconcile(
        IReadOnlyList<CullPerfSubmission> ledger, CullPerfEvent[] events, bool matchedCache = false)
    {
        var samples = new Dictionary<string, List<CullPerfSample>>();
        var failures = new List<string>();
        var operations = new Dictionary<long, long>();
        int completed = 0, cancelled = 0, superseded = 0, noOp = 0;
        for (var index = 0; index < ledger.Count; index++)
        {
            var input = ledger[index];
            var next = index + 1 < ledger.Count ? ledger[index + 1].Timestamp : long.MaxValue;
            var receipts = events.Where(item => item.Timestamp >= input.Timestamp &&
                item.Timestamp < next && (input.DirectSelection
                    ? item.Kind == "Selection" && item.ImageId == input.ImageId
                    : item.Kind is "Receipt" or "PickReceipt" or "NoOp")).ToArray();
            if (receipts.Length != 1)
            {
                failures.Add($"Input {input.Id}: expected one receipt, found {receipts.Length}.");
                continue;
            }
            var receipt = receipts[0];
            // Direct assignments have no command receipt or operation id; the
            // matching Selection acknowledges the input and its ledger id is unique.
            operations[input.Id] = input.DirectSelection ? input.Id : receipt.OperationId;
            Add("input-receipt-ms", input, receipt.Timestamp);
            if (receipt.Kind == "NoOp") { noOp++; continue; }
            foreach (var start in events.Where(item => item.Kind == "CacheEnqueueStart" &&
                item.WorkerId == receipt.WorkerId && item.Timestamp >= receipt.Timestamp && item.Timestamp < next))
            {
                var end = events.FirstOrDefault(item => item.Kind == "CacheEnqueueEnd" &&
                    item.OperationId == start.OperationId);
                if (end.Timestamp == 0) { failures.Add("Unclosed cache enqueue span."); continue; }
                if (!samples.TryGetValue("ui-enqueue-ms", out var spans)) samples["ui-enqueue-ms"] = spans = [];
                spans.Add(new(start.OperationId, Stopwatch.GetElapsedTime(start.Timestamp, end.Timestamp).TotalMilliseconds));
            }

            var selection = input.DirectSelection ? receipt : events.FirstOrDefault(item => item.Kind == "Selection" &&
                item.OperationId == receipt.OperationId);
            var matching = events.Where(item => item.Timestamp >= receipt.Timestamp &&
                item.ImageId == input.ImageId && (input.Pick
                    ? item.Timestamp < next
                    : item.Generation == selection.Generation)).ToArray();
            var feedback = matching.FirstOrDefault(item => item.Kind == (input.Pick ? "Feedback" : "SelectionFeedback"));
            if (feedback.Timestamp != 0) Add("input-feedback-ms", input, feedback.Timestamp);
            var terminal = matching.FirstOrDefault(item => input.Pick
                ? item.Kind == "Feedback"
                : IsPaint(item) || item.Kind is "Cancelled" or "Superseded" or "PaneSuperseded");
            if (terminal.Timestamp == 0)
            {
                failures.Add($"Input {input.Id}: no recorded terminal outcome.");
                continue;
            }
            if (terminal.Kind == "Cancelled") cancelled++;
            else if (terminal.Kind is "Superseded" or "PaneSuperseded") superseded++;
            else
            {
                completed++;
                Add(input.Pick ? "pick-feedback-ms" : "first-publication-ms", input, terminal.Timestamp);
                if (terminal.Kind == "CachedJpeg" && matchedCache) Add("matched-preview-ms", input, terminal.Timestamp);
            }
            var ready = matching.FirstOrDefault(item => item.Kind is "FreshRender" or "MatchedCacheReady");
            if (ready.Timestamp != 0) Add("accurate-ready-ms", input, ready.Timestamp);
        }
        if (ledger.Count != 0)
            foreach (var pair in WarmSamples(events, ledger[0].Timestamp)) samples[pair.Key] = pair.Value.ToList();
        return new(completed, cancelled, superseded, noOp, failures.ToArray(),
            samples.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()), operations);

        void Add(string metric, CullPerfSubmission input, long end)
        {
            if (!samples.TryGetValue(metric, out var values)) samples[metric] = values = [];
            values.Add(new(operations[input.Id], Stopwatch.GetElapsedTime(input.Timestamp, end).TotalMilliseconds));
        }
    }

    // Sample IDs here are one-based recorder positions, not receipt operation IDs.
    internal static Dictionary<string, CullPerfSample[]> WarmSamples(CullPerfEvent[] events, long firstInput)
    {
        var handoffs = new List<CullPerfSample>();
        var saves = new List<CullPerfSample>();
        var pendingWarms = new HashSet<long>();
        CullPerfEvent? previousWarm = null, outcome = null;
        for (var index = 0; index < events.Length; index++)
        {
            var item = events[index];
            if (item.Kind == "WarmEnqueue")
            {
                previousWarm = item;
                outcome = null;
                pendingWarms.Add(item.ImageId);
            }
            else if (previousWarm is { } warm && outcome == null && item.ImageId == warm.ImageId &&
                item.Value == 1 && item.Kind is "CacheWriteComplete" or "CacheWriteDropped")
                outcome = item;
            else if (item.Kind == "WarmHandoffResolved" && item.Value == 1 && outcome is { } write)
                handoffs.Add(new(index + 1, Stopwatch.GetElapsedTime(write.Timestamp, item.Timestamp).TotalMilliseconds));
            // The FIFO writer's first preview-tier save for a warmed image is the
            // warm's own write; a later departing-preview save is not, and a
            // write dropped before it reached the writer's hand has no save.
            if (item.Kind == "CacheSaveStart" && item.Value == 1 && pendingWarms.Remove(item.ImageId))
            {
                var saveEnd = events.Skip(index + 1).FirstOrDefault(next => next.Kind == "CacheSaveEnd" &&
                    next.OperationId == item.OperationId);
                if (saveEnd.Timestamp != 0)
                    saves.Add(new(index + 1, Stopwatch.GetElapsedTime(item.Timestamp, saveEnd.Timestamp).TotalMilliseconds));
            }
            else if (item.Kind == "CacheWriteDropped" && item.Value == 1) pendingWarms.Remove(item.ImageId);
        }
        var walks = Walks(events).ToArray();
        var initial = walks.FirstOrDefault();
        return new()
        {
            ["handoff-gap-ms"] = handoffs.ToArray(),
            ["warm-save-ms"] = saves.ToArray(),
            ["step-refill-ms"] = walks.Where(walk => walk.Start.Timestamp >= firstInput && walk.End != null)
                .Select(walk => Sample(walk.Id, walk.Start, walk.End!.Value)).ToArray(),
            ["initial-walk-ms"] = initial.Start.Timestamp < firstInput && initial.End is { } end && end.Timestamp < firstInput
                ? [Sample(initial.Id, initial.Start, end)] : []
        };

        static CullPerfSample Sample(long id, CullPerfEvent start, CullPerfEvent end) =>
            new(id, Stopwatch.GetElapsedTime(start.Timestamp, end.Timestamp).TotalMilliseconds);
    }

    internal static IEnumerable<(long Id, CullPerfEvent Start, CullPerfEvent? End)> Walks(CullPerfEvent[] events)
    {
        for (var index = 0; index < events.Length; index++)
        {
            if (events[index].Kind != "BufferRefill") continue;
            CullPerfEvent? launched = null, end = null;
            for (var next = index + 1; next < events.Length; next++)
            {
                if (events[next].Kind == "BufferRefill") break;
                if (launched == null && events[next].Kind == "WalkComplete" &&
                    events[next].ImageId == events[index].ImageId)
                    launched = events[next];
                // The walk loop records WalkComplete when its last warm launches;
                // the buffer is refilled only when that warm publishes, so a
                // walk whose last warm is cancelled has no end.
                else if (launched != null && events[next].Kind == "WarmComplete")
                    end = events[next];
            }
            yield return (index + 1, events[index], end);
        }
    }

}

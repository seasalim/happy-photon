using System.Diagnostics;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal sealed record CullPerfSubmission(long Id, long Timestamp, long ImageId, bool Pick);
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
                item.Timestamp < next && item.Kind is "Receipt" or "PickReceipt" or "NoOp").ToArray();
            if (receipts.Length != 1)
            {
                failures.Add($"Input {input.Id}: expected one receipt, found {receipts.Length}.");
                continue;
            }
            var receipt = receipts[0];
            operations[input.Id] = receipt.OperationId;
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

            var selection = events.FirstOrDefault(item => item.Kind == "Selection" &&
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
        return new(completed, cancelled, superseded, noOp, failures.ToArray(),
            samples.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()), operations);

        void Add(string metric, CullPerfSubmission input, long end)
        {
            if (!samples.TryGetValue(metric, out var values)) samples[metric] = values = [];
            values.Add(new(operations[input.Id], Stopwatch.GetElapsedTime(input.Timestamp, end).TotalMilliseconds));
        }
    }
}

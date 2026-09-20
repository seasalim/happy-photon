using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal static class CullPerfDwell
{
    // Develop must freshly render every step so a rendered preview departs;
    // the loupe legitimately paints a matched warm entry without a fresh render,
    // so there any accepted paint before the next input counts.
    internal static int Validate(IReadOnlyList<CullPerfSubmission> ledger,
        CullPerfEvent[] events, long initialImageId, List<string> failures,
        bool requireFreshRender = true)
    {
        if (ledger.Count == 0) return 0;
        var outcome = requireFreshRender ? "fresh render" : "paint";
        if (!events.Any(item => Counts(item) && item.ImageId == initialImageId &&
            item.Timestamp < ledger[0].Timestamp))
            failures.Add($"Dwell initial image had no {outcome}.");
        var completed = 0;
        for (var index = 0; index < ledger.Count; index++)
        {
            var input = ledger[index];
            var next = index + 1 < ledger.Count ? ledger[index + 1].Timestamp : long.MaxValue;
            var selection = events.FirstOrDefault(item => item.Kind == "Selection" &&
                item.ImageId == input.ImageId && item.Timestamp >= input.Timestamp && item.Timestamp < next);
            if (selection.Timestamp != 0 && events.Any(item => Counts(item) &&
                item.ImageId == input.ImageId && item.Generation == selection.Generation &&
                item.Timestamp >= selection.Timestamp && item.Timestamp < next))
                completed++;
            else
                failures.Add($"Dwell input {input.Id}: no {outcome} before the next input.");
        }
        return completed;

        bool Counts(CullPerfEvent item) =>
            item.Kind == "FreshRender" || !requireFreshRender && item.Kind == "CachedJpeg";
    }
}

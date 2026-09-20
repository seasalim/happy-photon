using System.Diagnostics;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CullPerfWarmMetricTests
{
    [Theory]
    [InlineData("CacheWriteComplete")]
    [InlineData("CacheWriteDropped")]
    public void HandoffUsesFirstPreviewOutcomeOfPreviousEnqueueAndOnlyWaitedResolves(string outcome)
    {
        CullPerfEvent[] events =
        [
            E(1, outcome, 10, 1), // An older write for the same image is ineligible.
            E(2, "WarmEnqueue", 10),
            E(3, outcome, 10, 2), // Rendered thumbnail tier is ineligible.
            E(4, outcome, 99, 1), // Unrelated preview write is ineligible.
            E(5, outcome, 10, 1),
            E(6, outcome, 10, 1), // A later departing-preview write is ineligible.
            E(8, "WarmHandoffResolved", 11, 1),
            E(9, "WarmEnqueue", 11),
            E(10, outcome, 11, 1),
            E(12, "WarmHandoffResolved", 12, 0), // No wait, no sample.
            E(13, "WarmEnqueue", 10), // Same image, new write instance.
            E(15, outcome, 10, 1),
            E(18, "WarmHandoffResolved", 13, 1),
            E(19, "WarmEnqueue", 14),
            E(20, "WarmHandoffResolved", 15, 1), // No outcome, no invented gap.
            E(21, outcome, 14, 1)
        ];
        var samples = CullPerfLedger.WarmSamples(events, 1)["handoff-gap-ms"];
        Assert.Equal(2, samples.Length);
        Assert.All(samples, sample => Assert.Equal(Stopwatch.GetElapsedTime(5, 8).TotalMilliseconds, sample.Value));
        Assert.Equal(new long[] { 7, 13 }, samples.Select(sample => sample.OperationId));
    }

    [Fact]
    public void WalksExcludeCancellationAndKeepInitialWalkSeparate()
    {
        CullPerfEvent[] events =
        [
            E(1, "BufferRefill", 1), E(5, "WalkComplete", 1),
            E(7, "WarmComplete", 5), // The launched fifth warm publishing ends the walk.
            E(11, "BufferRefill", 2), // Cancelled before the next walk.
            E(21, "BufferRefill", 3), E(23, "WarmComplete", 8), // Before WalkComplete: not the end.
            E(25, "WalkComplete", 3),
            E(26, "WalkComplete", 3), // Does not create a second sample.
            E(28, "WarmComplete", 9),
            E(31, "BufferRefill", 4) // Unfinished at snapshot.
        ];
        var samples = CullPerfLedger.WarmSamples(events, 10);
        Assert.Equal(Stopwatch.GetElapsedTime(1, 7).TotalMilliseconds,
            Assert.Single(samples["initial-walk-ms"]).Value);
        var step = Assert.Single(samples["step-refill-ms"]);
        Assert.Equal(5, step.OperationId);
        Assert.Equal(Stopwatch.GetElapsedTime(21, 28).TotalMilliseconds, step.Value);
        var counters = CullPerfCounters.Derive(events, 10);
        Assert.Equal(1, counters["step-walks-completed"]);
        Assert.Equal(2, counters["step-walks-excluded"]);
        Assert.Equal(2, counters["walks-without-complete"]);
    }

    [Fact]
    public void WalkWhoseLastWarmIsCancelledHasNoRefillSample()
    {
        CullPerfEvent[] events =
        [
            E(1, "BufferRefill", 1), E(3, "WarmComplete", 5), E(5, "WalkComplete", 1),
            E(11, "BufferRefill", 2), E(13, "WalkComplete", 2), E(15, "WarmComplete", 7),
            E(21, "BufferRefill", 3)
        ];
        var samples = CullPerfLedger.WarmSamples(events, 10);
        Assert.Empty(samples["initial-walk-ms"]);
        Assert.Equal(Stopwatch.GetElapsedTime(11, 15).TotalMilliseconds,
            Assert.Single(samples["step-refill-ms"]).Value);
        var counters = CullPerfCounters.Derive(events, 10);
        Assert.Equal(1, counters["step-walks-completed"]);
        Assert.Equal(1, counters["step-walks-excluded"]);
    }

    [Fact]
    public void InitialWalkMustFinishBeforeInputAndMissingWalksStayMissing()
    {
        Assert.Empty(CullPerfLedger.WarmSamples([], 10)["initial-walk-ms"]);
        var samples = CullPerfLedger.WarmSamples(
            [E(1, "BufferRefill", 1), E(11, "WalkComplete", 1)], 10);
        Assert.Empty(samples["initial-walk-ms"]);
        Assert.Empty(samples["step-refill-ms"]);
    }

    private static CullPerfEvent E(long time, string kind, long image, long value = 0) =>
        new(time, kind, 0, image, 0, 1, value);
}

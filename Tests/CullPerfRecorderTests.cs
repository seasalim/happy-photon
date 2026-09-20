using System.Diagnostics;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CullPerfRecorderTests
{
    [Fact]
    public void RingRetainsOrderedTailAndReportsEveryLostEvent()
    {
        var recorder = new CullPerfRecorder(2);
        recorder.Record("first");
        recorder.Record("second", 42, 7, 9);
        recorder.Record("third");
        var events = recorder.Snapshot();
        Assert.Equal(1, recorder.LostEvents);
        Assert.Equal(["second", "third"], events.Select(item => item.Kind));
        Assert.True(events[0].Timestamp <= events[1].Timestamp);
        Assert.Equal(42, events[0].ImageId);
        Assert.Equal(7, events[0].Generation);
        Assert.Equal(9, events[0].OperationId);
    }

    [Fact]
    public void ConcurrentWritersKeepAllEventsUntilCapacity()
    {
        var recorder = new CullPerfRecorder(10000);
        Parallel.For(0, 10000, i => recorder.Record("worker", i));
        var events = recorder.Snapshot();
        Assert.Equal(10000, events.Select(item => item.ImageId).Distinct().Count());
        Assert.Equal(0, recorder.LostEvents);
        Assert.True(events.Zip(events.Skip(1)).All(pair => pair.First.Timestamp <= pair.Second.Timestamp));
    }

    [Fact]
    public void DisabledPathAllocatesNothing()
    {
        CullPerfRecorder? recorder = null;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) recorder?.Record("Receipt");
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void OperationIdsAreExplicitAndIndependentOfKindNames()
    {
        var recorder = new CullPerfRecorder();
        var operation = recorder.Record("arbitrary", operation: -1);
        recorder.Record("Receipt");
        recorder.Record("child", operation: operation);
        Assert.Equal(new long[] { operation, 0, operation },
            recorder.Snapshot().Select(item => item.OperationId));
    }

    [Fact]
    public void NativeCountersTrackOverlapAndSupersessionUntilEachWorkerReturns()
    {
        CullPerfEvent[] events =
        [
            new(1, "NativeStart", 0, 42, 0, 1, 0),
            new(2, "NativeStart", 0, 42, 0, 2, 0),
            new(3, "NativeStart", 0, 43, 0, 3, 0),
            new(4, "Superseded", 0, 42, 7, 4, 0),
            new(5, "Superseded", 0, 42, 7, 4, 0),
            new(6, "NativeEnd", 0, 42, 0, 1, 0)
        ];
        var counters = CullPerfCounters.Derive(events);
        Assert.Equal(3, counters["native-active-maximum"]);
        Assert.Equal(1, counters["duplicate-decodes"]);
        Assert.Equal(2, counters["superseded-native-still-running-maximum"]);
        Assert.Equal(1, counters["superseded-native-still-running-at-end"]);
        counters = CullPerfCounters.Derive([.. events, new(7, "NativeEnd", 0, 42, 0, 2, 0)]);
        Assert.Equal(0, counters["superseded-native-still-running-at-end"]);
        Assert.Equal(1, counters["native-active-at-end"]);
    }

    [Theory]
    [InlineData("FreshRender", 1, 0, 0)]
    [InlineData("Cancelled", 0, 1, 0)]
    [InlineData("Superseded", 0, 0, 1)]
    public void LedgerUsesRecordedOutcomes(string kind, int completed, int cancelled, int superseded)
    {
        var recorder = new CullPerfRecorder();
        recorder.Record("prior");
        var input = new CullPerfSubmission(1, Stopwatch.GetTimestamp(), 42, false);
        var operation = recorder.Record("Receipt", operation: -1);
        recorder.Record("Selection", 42, 10, operation);
        recorder.Record(kind, 42, 10, operation);
        var result = CullPerfLedger.Reconcile([input], recorder.Snapshot());
        Assert.Empty(result.Failures);
        Assert.Equal(operation, result.Samples["input-receipt-ms"].Single().OperationId);
        Assert.Equal(completed, result.Completed);
        Assert.Equal(cancelled, result.Cancelled);
        Assert.Equal(superseded, result.Superseded);
    }

    [Fact]
    public void MissingTerminalIsNotInferredFromLaterSelection()
    {
        var recorder = new CullPerfRecorder();
        var input = new CullPerfSubmission(1, Stopwatch.GetTimestamp(), 42, false);
        recorder.Record("Receipt", operation: -1);
        var result = CullPerfLedger.Reconcile([input], recorder.Snapshot());
        Assert.Single(result.Failures);
        Assert.Equal(0, result.Completed + result.Superseded + result.Cancelled);
    }
}

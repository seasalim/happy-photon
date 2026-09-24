using System.Diagnostics;

namespace HappyPhoton.Tests;

// Rebuilt per render. Only segment-index data is retained; no pixel-sized mask or cache.
internal sealed class LocalsBrushEvaluation
{
    private readonly LocalsBrushOptimizedGrid[] grids;
    private readonly int width, height;
    internal int Count => grids.Length;
    internal double BuildMilliseconds { get; }
    internal long AllocatedBytes { get; }
    internal string Report => $"build_ms={BuildMilliseconds:R} build_caller_alloc_bytes={AllocatedBytes} " +
        $"payload_bytes={grids.Sum(g => g.PayloadBytes)} segments={grids.Sum(g => g.SegmentCount)} " +
        $"entries={grids.Sum(g => g.EntryCount)} worst_segments_per_cell={grids.Max(g => g.WorstSegmentsPerCell)} " +
        $"dims={string.Join(';', grids.Select(g => $"{g.Columns}x{g.Rows}"))}";

    internal LocalsBrushEvaluation(BrushDocument[] documents, int width, int height)
    {
        (this.width, this.height) = (width, height);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        grids = documents.Select(d => new LocalsBrushOptimizedGrid(d, width, height)).ToArray();
        BuildMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    }

    internal double Weight(int local, int pixel) =>
        grids[local].Weight((pixel % width + .5) / width, (pixel / width + .5) / height);
}

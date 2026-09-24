using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal sealed class LocalsBrushIndexMeasurement
{
    internal double BuildMilliseconds { get; }
    internal long AllocatedBytes { get; }
    internal RenderLocals Plan { get; }
    internal string Report => $"build_ms={BuildMilliseconds:R} build_caller_alloc_bytes={AllocatedBytes} production=True";
    internal LocalsBrushIndexMeasurement(EditSettings settings, int width, int height)
    {
        var frame = new LocalsFrame(width, height, 0, 0, 1, 1);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        Plan = RenderLocals.Create(settings, default, width, height, frame)!;
        BuildMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    }
}

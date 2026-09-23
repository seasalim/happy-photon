using System.Diagnostics;
using ImageMagick;

namespace HappyPhoton.Services;

/// <summary>
/// Opt-in per-stage render timing behind HAPPY_PHOTON_PERF. With the switch off
/// or no listener attached, <see cref="Begin"/> returns an empty mark and
/// <see cref="End"/> returns immediately: no timestamp, no allocation.
/// Allocation is counted on the calling thread, which owns each stage's
/// whole-frame Q16 buffer; worker-thread scratch is not included.
/// </summary>
internal static class RenderStageProbe
{
    private static readonly bool Enabled = ImageServiceHelpers.PerfLoggingEnabled;
    private static Action<RenderStageSample>? _listener;

    internal static IDisposable Listen(Action<RenderStageSample> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        if (Interlocked.CompareExchange(ref _listener, listener, null) != null)
        {
            throw new InvalidOperationException(
                "A render stage listener is already attached.");
        }
        return new Subscription(listener);
    }

    internal static RenderStageMark Begin() =>
        Enabled && Volatile.Read(ref _listener) != null
            ? new(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread())
            : default;

    internal static void End(RenderStageMark mark, string stage, MagickImage image)
    {
        if (mark.Timestamp == 0) return;
        var elapsed = Stopwatch.GetElapsedTime(mark.Timestamp);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - mark.AllocatedBytes;
        Volatile.Read(ref _listener)?.Invoke(new RenderStageSample(
            stage,
            elapsed,
            allocated,
            checked((int)image.Width),
            checked((int)image.Height),
            checked((int)image.ChannelCount)));
    }

    private sealed class Subscription(Action<RenderStageSample> listener) : IDisposable
    {
        public void Dispose() =>
            Interlocked.CompareExchange(ref _listener, null, listener);
    }
}

internal readonly record struct RenderStageMark(long Timestamp, long AllocatedBytes);

internal readonly record struct RenderStageSample(
    string Stage,
    TimeSpan Elapsed,
    long CallerAllocatedBytes,
    int Width,
    int Height,
    int Channels)
{
    internal long FrameBytes => (long)Width * Height * Channels * sizeof(ushort);
}

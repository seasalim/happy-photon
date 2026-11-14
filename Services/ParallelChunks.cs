using System.Runtime.CompilerServices;

namespace HappyPhoton.Services;

// Worker counts and chunk bounds for Parallel.For over a flat range; called per worker or per row, never per pixel.
internal static class ParallelChunks
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int WorkerCount(int count, int grain) =>
        Math.Min(Environment.ProcessorCount, Math.Max(1, count / grain));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int WorkerCountCeiling(int count, int grain) =>
        Math.Min(Environment.ProcessorCount, Math.Max(1, (count + grain - 1) / grain));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static (int Start, int End) ChunkRange(int count, int worker, int workers) =>
        ((int)((long)count * worker / workers),
            (int)((long)count * (worker + 1) / workers));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CeilingDivide(long numerator, int denominator) =>
        checked((int)((numerator + denominator - 1) / denominator));
}

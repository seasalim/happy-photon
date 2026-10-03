using System.Buffers;

namespace HappyPhoton.Services;

public static partial class HorizonDetection
{
    // A call owns each rental until its last consumer finishes, including on failure.
    // Stages initialize the ranges they read; pooled capacity is never a pixel count.
    private sealed class Scratch<T>(int count) : IDisposable
    {
        public T[] Values { get; } = ArrayPool<T>.Shared.Rent(count);

        public void Dispose() => ArrayPool<T>.Shared.Return(Values);
    }
}

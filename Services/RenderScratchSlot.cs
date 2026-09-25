namespace HappyPhoton.Services;

internal sealed class RenderScratchSlot<T>
{
    private T[]? _scratch;

    internal T[] Take(int length)
    {
        var scratch = Interlocked.Exchange(ref _scratch, null);
        return scratch is not null && scratch.Length >= length
            ? scratch : GC.AllocateUninitializedArray<T>(length);
    }

    internal void Return(T[] scratch)
    {
        var retained = Volatile.Read(ref _scratch);
        while (retained is null || retained.Length < scratch.Length)
        {
            var previous = Interlocked.CompareExchange(ref _scratch, scratch, retained);
            if (ReferenceEquals(previous, retained)) return;
            retained = previous;
        }
    }
}

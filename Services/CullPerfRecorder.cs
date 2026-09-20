using System.Diagnostics;

namespace HappyPhoton.Services;

internal readonly record struct CullPerfEvent(long Timestamp, string Kind, long OperationId,
    long ImageId, long Generation, int WorkerId, long Value);

internal sealed class CullPerfRecorder(int capacity = 16384)
{
    private readonly CullPerfEvent[] _events = new CullPerfEvent[capacity > 0
        ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity))];
    private readonly object _sync = new();
    private long _sequence;
    private int _count;
    internal long LostEvents { get; private set; }
    internal long Record(string kind, long image = 0, long generation = 0,
        long operation = 0, long value = 0)
    {
        lock (_sync)
        {
            var sequence = ++_sequence;
            if (_count == _events.Length) LostEvents++;
            else _count++;
            _events[(sequence - 1) % _events.Length] = new(Stopwatch.GetTimestamp(),
                kind, operation == -1 ? sequence : operation, image, generation,
                Environment.CurrentManagedThreadId, value);
            return sequence;
        }
    }

    internal CullPerfEvent[] Snapshot()
    {
        lock (_sync)
            return Enumerable.Range(0, _count)
                .Select(i => _events[(_sequence - _count + i) % _events.Length]).ToArray();
    }
}

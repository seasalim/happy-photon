using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal sealed class TestSourceAvailabilityService : ISourceAvailabilityService
{
    private int _callCount;

    internal TestSourceAvailabilityService(SourceAvailability availability) =>
        Availability = availability;

    internal SourceAvailability Availability { get; set; }
    internal Func<string, SourceAvailability>? Resolver { get; set; }
    internal int CallCount => Volatile.Read(ref _callCount);

    public SourceAvailability GetAvailability(string path)
    {
        Interlocked.Increment(ref _callCount);
        return Resolver?.Invoke(path) ?? Availability;
    }
}

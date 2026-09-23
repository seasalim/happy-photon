using System.Diagnostics;

namespace HappyPhoton.Services;

internal struct RawPreviewPerformanceTrace
{
    private readonly Stopwatch _stopwatch;
    private readonly string _filePath;
    private readonly string _loader;
    private readonly bool _enabled;
    private long _lastElapsedMs;

    internal RawPreviewPerformanceTrace(
        Stopwatch stopwatch,
        string filePath,
        bool preview,
        string loader = nameof(RawBaseLoader))
    {
        _stopwatch = stopwatch;
        _filePath = filePath;
        _loader = loader;
        _enabled = preview && ImageServiceHelpers.PerfLoggingEnabled;
    }

    internal void Mark(string stage)
    {
        if (!_enabled)
        {
            return;
        }

        var totalElapsedMs = _stopwatch.ElapsedMilliseconds;
        ImageServiceHelpers.LogPerformance(
            _loader,
            $"Preview.{stage}",
            totalElapsedMs - _lastElapsedMs,
            _filePath,
            $"total={totalElapsedMs}");
        _lastElapsedMs = totalElapsedMs;
    }
}

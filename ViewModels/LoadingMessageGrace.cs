namespace HappyPhoton.ViewModels;

internal sealed class LoadingMessageGrace(TimeProvider clock, Action<bool> publish) : IDisposable
{
    internal const int GraceMilliseconds = 300;
    private readonly object _sync = new();
    private CancellationTokenSource? _interval;
    private bool _disposed;

    public void Update(bool condition)
    {
        lock (_sync)
        {
            if ((_disposed && condition) || condition == (_interval != null)) return;
            var previous = _interval;
            var interval = _interval = condition ? new CancellationTokenSource() : null;
            previous?.Cancel();
            previous?.Dispose();
            if (interval == null) { publish(false); return; }
            _ = DebouncedAction.RunAsync("loading message", TimeSpan.FromMilliseconds(GraceMilliseconds),
                interval.Token, () =>
                {
                    // Serialize publication with invalidation, including dispatcher-less callers.
                    lock (_sync)
                        if (!_disposed && ReferenceEquals(_interval, interval)) publish(true);
                    return Task.CompletedTask;
                }, timeProvider: clock);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            Update(false);
        }
    }
}

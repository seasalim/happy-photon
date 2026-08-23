namespace HappyPhoton.ViewModels;

internal sealed class ThumbnailPumpAdmissionGate
{
    private readonly object _sync = new();
    private TaskCompletionSource? _resume;

    internal bool IsPaused
    {
        get
        {
            lock (_sync) return _resume != null;
        }
    }

    internal void SetPaused(bool paused)
    {
        TaskCompletionSource? release = null;
        lock (_sync)
        {
            if (paused)
            {
                _resume ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
            else
            {
                release = _resume;
                _resume = null;
            }
        }
        release?.TrySetResult();
    }

    internal ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_resume == null) return ValueTask.CompletedTask;
        }
        return WaitUntilAdmittedAsync(cancellationToken);
    }

    private async ValueTask WaitUntilAdmittedAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Task wait;
            lock (_sync)
            {
                if (_resume == null)
                {
                    return;
                }
                wait = _resume.Task;
            }
            await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

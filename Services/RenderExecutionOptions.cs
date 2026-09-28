namespace HappyPhoton.Services;

internal readonly record struct RenderExecutionOptions(
    CancellationToken CancellationToken,
    int MaxDegreeOfParallelism,
    Action<string>? StageStarted,
    Action? CancellationObserved = null)
{
    internal Func<int>? WorkerBudget { get; init; }

    internal Action<int>? WorkersSelected { get; init; }

    // Reports a worker after its first pixel batch, then on exit (including cancellation).
    internal Action<int, int, bool>? RawCrossingWorkerProgress { get; init; }

    internal static RenderExecutionOptions Resting(
        CancellationToken cancellationToken,
        int maxDegreeOfParallelism = 2,
        Action<string>? stageStarted = null,
        Action? cancellationObserved = null) =>
        new(
            cancellationToken,
            Math.Max(1, maxDegreeOfParallelism),
            stageStarted,
            cancellationObserved);

    internal ParallelOptions ParallelOptions => new()
    {
        CancellationToken = CancellationToken,
        MaxDegreeOfParallelism = CapWorkers(MaxDegreeOfParallelism)
    };

    internal int CapWorkers(int workers)
    {
        ThrowIfCancellationRequested();
        var selected = Math.Min(Math.Max(1, workers),
            Math.Max(1, WorkerBudget?.Invoke() ?? MaxDegreeOfParallelism));
        WorkersSelected?.Invoke(selected);
        return selected;
    }

    internal void ThrowIfCancellationRequested()
    {
        CancellationObserved?.Invoke();
        CancellationToken.ThrowIfCancellationRequested();
    }

    internal void ReportStage(string stage) => StageStarted?.Invoke(stage);
}

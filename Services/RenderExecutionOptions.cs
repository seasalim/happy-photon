namespace HappyPhoton.Services;

internal readonly record struct RenderExecutionOptions(
    CancellationToken CancellationToken,
    int MaxDegreeOfParallelism,
    Action<string>? StageStarted,
    Action? CancellationObserved = null)
{
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
        MaxDegreeOfParallelism = MaxDegreeOfParallelism
    };

    internal int CapWorkers(int workers) =>
        Math.Min(Math.Max(1, workers), MaxDegreeOfParallelism);

    internal void ThrowIfCancellationRequested()
    {
        CancellationObserved?.Invoke();
        CancellationToken.ThrowIfCancellationRequested();
    }

    internal void ReportStage(string stage) => StageStarted?.Invoke(stage);
}

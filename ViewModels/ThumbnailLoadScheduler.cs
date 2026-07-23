using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

internal sealed class ThumbnailLoadScheduler : IDisposable
{
    private readonly object _sync = new();
    private readonly PriorityQueue<QueueEntry, int> _queue = new();
    private readonly Dictionary<ImageFile, int> _queuedPriorities = new();
    private readonly HashSet<ImageFile> _inFlight = new();
    private readonly SemaphoreSlim _available = new(0);
    private readonly Func<ImageFile, CancellationToken, Task> _loadAsync;
    private readonly CancellationToken _cancellationToken;
    private readonly Task[] _workers;

    public ThumbnailLoadScheduler(
        int workerCount,
        Func<ImageFile, CancellationToken, Task> loadAsync,
        CancellationToken cancellationToken)
    {
        _loadAsync = loadAsync;
        _cancellationToken = cancellationToken;
        _workers = Enumerable.Range(0, workerCount)
            .Select(_ => WorkerLoopAsync())
            .ToArray();
        Completion = Task.WhenAll(_workers);
    }

    public Task Completion { get; }

    public void Enqueue(IEnumerable<(ImageFile Image, int Priority)> requests)
    {
        var added = 0;
        lock (_sync)
        {
            foreach (var (image, priority) in requests)
            {
                if (image.Thumbnail != null || image.ThumbnailLoadFailed ||
                    _inFlight.Contains(image))
                {
                    continue;
                }
                if (_queuedPriorities.TryGetValue(image, out var currentPriority) &&
                    currentPriority <= priority)
                {
                    continue;
                }

                _queuedPriorities[image] = priority;
                _queue.Enqueue(new QueueEntry(image, priority), priority);
                added++;
            }
        }

        if (added > 0) _available.Release(added);
    }

    private async Task WorkerLoopAsync()
    {
        try
        {
            while (true)
            {
                await _available.WaitAsync(_cancellationToken);
                var entry = TakeNext();
                if (entry == null) continue;
                try
                {
                    await _loadAsync(entry.Image, _cancellationToken);
                }
                finally
                {
                    lock (_sync)
                    {
                        _inFlight.Remove(entry.Image);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
        }
    }

    private QueueEntry? TakeNext()
    {
        lock (_sync)
        {
            while (_queue.TryDequeue(out var candidate, out var priority))
            {
                if (!_queuedPriorities.TryGetValue(candidate.Image, out var current) ||
                    current != priority)
                {
                    continue;
                }

                _queuedPriorities.Remove(candidate.Image);
                _inFlight.Add(candidate.Image);
                return candidate;
            }
        }
        return null;
    }

    public void Dispose() => _available.Dispose();

    private sealed record QueueEntry(ImageFile Image, int Priority);
}

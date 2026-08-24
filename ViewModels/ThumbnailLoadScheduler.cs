using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

internal readonly record struct ThumbnailLoadRequest(
    ImageFile Image,
    ThumbnailSizeRequest Size,
    int Priority);

internal sealed class ThumbnailLoadScheduler : IDisposable
{
    private readonly object _sync = new();
    private readonly PriorityQueue<QueueEntry, int> _queue = new();
    private readonly Dictionary<ImageFile, QueueEntry> _queued = new();
    private readonly Dictionary<ImageFile, ThumbnailSizeRequest> _inFlight = new();
    private readonly Dictionary<ImageFile, ThumbnailSizeRequest> _desired = new();
    private readonly Dictionary<ImageFile, ThumbnailSizeRequest> _followUps = new();
    private readonly SemaphoreSlim _available = new(0);
    private readonly Func<ImageFile, ThumbnailSizeRequest, CancellationToken, Task>
        _loadAsync;
    private readonly CancellationToken _cancellationToken;
    private readonly Task[] _workers;

    public ThumbnailLoadScheduler(
        int workerCount,
        Func<ImageFile, ThumbnailSizeRequest, CancellationToken, Task> loadAsync,
        CancellationToken cancellationToken)
    {
        _loadAsync = loadAsync;
        _cancellationToken = cancellationToken;
        _workers = Enumerable.Range(0, workerCount)
            .Select(_ => WorkerLoopAsync())
            .ToArray();
        Completion = Task.WhenAll(_workers);
    }

    public ThumbnailLoadScheduler(
        int workerCount,
        Func<ImageFile, CancellationToken, Task> loadAsync,
        CancellationToken cancellationToken) : this(
            workerCount,
            (image, _, token) => loadAsync(image, token),
            cancellationToken)
    {
    }

    public Task Completion { get; }

    public void Enqueue(IEnumerable<(ImageFile Image, int Priority)> requests) =>
        Enqueue(requests.Select(request => new ThumbnailLoadRequest(
            request.Image,
            ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium),
            request.Priority)));

    public void Enqueue(
        IEnumerable<ThumbnailLoadRequest> requests,
        bool force = false,
        bool replaceDesired = false)
    {
        var added = 0;
        lock (_sync)
        {
            foreach (var request in requests)
            {
                var effective = request;
                if (replaceDesired ||
                    !_desired.TryGetValue(request.Image, out var desired) ||
                    IsLarger(request.Size, desired))
                {
                    _desired[request.Image] = request.Size;
                }
                else
                {
                    effective = request with { Size = desired };
                }

                if (ShouldSkip(effective.Image, effective.Size))
                {
                    _desired.Remove(effective.Image);
                    _followUps.Remove(effective.Image);
                    continue;
                }

                if (_inFlight.TryGetValue(effective.Image, out var inFlight))
                {
                    if (force || IsLarger(effective.Size, inFlight))
                    {
                        _followUps[effective.Image] = effective.Size;
                    }
                    continue;
                }

                if (_queued.TryGetValue(effective.Image, out var queued) &&
                    !IsLarger(effective.Size, queued.Size) &&
                    queued.Priority <= effective.Priority)
                {
                    continue;
                }

                var entry = new QueueEntry(
                    effective.Image,
                    effective.Size,
                    Math.Min(effective.Priority, queued?.Priority ?? effective.Priority));
                _queued[effective.Image] = entry;
                _queue.Enqueue(entry, entry.Priority);
                added++;
            }
        }

        if (added > 0) _available.Release(added);
    }

    public void ReplaceQueued(IEnumerable<ThumbnailLoadRequest> requests)
    {
        lock (_sync)
        {
            _queue.Clear();
            _queued.Clear();
            _desired.Clear();
            _followUps.Clear();
        }

        Enqueue(requests, replaceDesired: true);
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
                ThumbnailSizeRequest? followUp = null;
                try
                {
                    await _loadAsync(entry.Image, entry.Size, _cancellationToken);
                }
                finally
                {
                    lock (_sync)
                    {
                        _inFlight.Remove(entry.Image);
                        if (_followUps.Remove(entry.Image, out var pending))
                        {
                            followUp = pending;
                        }
                        else
                        {
                            _desired.Remove(entry.Image);
                        }
                    }
                }

                if (followUp is { } request)
                {
                    Enqueue([new ThumbnailLoadRequest(entry.Image, request, entry.Priority)]);
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
            while (_queue.TryDequeue(out var candidate, out _))
            {
                if (!_queued.TryGetValue(candidate.Image, out var current) ||
                    current != candidate)
                {
                    continue;
                }

                _queued.Remove(candidate.Image);
                if (ShouldSkip(candidate.Image, candidate.Size))
                {
                    _desired.Remove(candidate.Image);
                    continue;
                }
                _inFlight[candidate.Image] = candidate.Size;
                return candidate;
            }
        }
        return null;
    }

    private static bool ShouldSkip(
        ImageFile image,
        ThumbnailSizeRequest request)
    {
        if (image.ThumbnailSatisfies(request)) return true;
        return image.Thumbnail == null
            ? image.ThumbnailLoadFailed || image.ThumbnailDeferredForHydration
            : image.ThumbnailUpgradeDeferredDimension >= request.GenerationDimension ||
                image.ThumbnailUpgradeFailedDimension >= request.GenerationDimension;
    }

    private static bool IsLarger(
        ThumbnailSizeRequest left,
        ThumbnailSizeRequest right) =>
        left.MinimumDimension > right.MinimumDimension ||
        left.MinimumDimension == right.MinimumDimension &&
        left.GenerationDimension > right.GenerationDimension;

    internal int DesiredCount
    {
        get
        {
            lock (_sync) return _desired.Count;
        }
    }

    public void Dispose() => _available.Dispose();

    private sealed record QueueEntry(
        ImageFile Image,
        ThumbnailSizeRequest Size,
        int Priority);
}

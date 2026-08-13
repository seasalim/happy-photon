namespace HappyPhoton.ViewModels;

public enum BackgroundActivityKind
{
    Export,
    CaptureTimes,
    Preview,
    Thumbnails,
    Metadata,
    CacheWrites
}

public sealed record BackgroundProgress(int Processed, int Total);

public sealed record ExportActivitySnapshot(
    int ScopeCount,
    int Current,
    int Total);

public sealed record BackgroundActivitySnapshot(
    int ThumbnailCount,
    int PreviewCount,
    int CacheWriteCount,
    int MetadataCount,
    BackgroundProgress? CaptureTimes,
    ExportActivitySnapshot? Export)
{
    public static BackgroundActivitySnapshot Empty { get; } =
        new(0, 0, 0, 0, null, null);

    public bool IsEmpty =>
        ThumbnailCount <= 0 &&
        PreviewCount <= 0 &&
        CacheWriteCount <= 0 &&
        MetadataCount <= 0 &&
        CaptureTimes == null &&
        Export == null;
}

public sealed record BackgroundActivityDisplay(
    bool IsVisible,
    string Label,
    string Tooltip,
    int ActiveKindCount,
    bool ShowProgress,
    int ProgressValue,
    int ProgressMaximum)
{
    public static BackgroundActivityDisplay Hidden { get; } =
        new(false, string.Empty, string.Empty, 0, false, 0, 1);
}

public sealed class BackgroundActivityAggregator
{
    public static readonly TimeSpan ShowDelay = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(600);

    private DateTimeOffset? _nonEmptySince;
    private DateTimeOffset? _emptySince;
    private long _quietEpoch;
    private bool _isVisible;
    private BackgroundActivityDisplay _lastDisplay =
        BackgroundActivityDisplay.Hidden;

    public BackgroundActivityDisplay Aggregate(
        BackgroundActivitySnapshot snapshot,
        DateTimeOffset now,
        long activityEpoch)
    {
        if (snapshot.IsEmpty)
        {
            _nonEmptySince = null;
            if (_emptySince == null || _quietEpoch != activityEpoch)
            {
                _emptySince = now;
                _quietEpoch = activityEpoch;
            }
            if (_isVisible && now - _emptySince >= HideDelay)
            {
                _isVisible = false;
            }
        }
        else
        {
            _emptySince = null;
            _nonEmptySince ??= now;
            if (!_isVisible && now - _nonEmptySince >= ShowDelay)
            {
                _isVisible = true;
            }
        }

        if (!snapshot.IsEmpty)
        {
            _lastDisplay = Format(snapshot);
        }
        return _lastDisplay with { IsVisible = _isVisible };
    }

    public bool CanStop(
        BackgroundActivitySnapshot snapshot,
        DateTimeOffset now,
        long activityEpoch) =>
        !_isVisible &&
        snapshot.IsEmpty &&
        _emptySince != null &&
        _quietEpoch == activityEpoch &&
        now - _emptySince >= HideDelay;

    private static BackgroundActivityDisplay Format(
        BackgroundActivitySnapshot snapshot)
    {
        var activities = new List<ActivityText>(6);
        if (snapshot.Export is { } export)
        {
            var noun = export.Total == 1 ? "photo" : "photos";
            activities.Add(new ActivityText(
                BackgroundActivityKind.Export,
                export.Current <= 0
                    ? $"Exporting — preparing {export.Total:N0} {noun}"
                    : $"Exporting — {export.Current:N0} / {export.Total:N0}"));
        }
        if (snapshot.CaptureTimes is { } burst)
        {
            activities.Add(new ActivityText(
                BackgroundActivityKind.CaptureTimes,
                $"Capture times — {burst.Processed:N0} / {burst.Total:N0}"));
        }
        if (snapshot.PreviewCount > 0)
        {
            activities.Add(new ActivityText(
                BackgroundActivityKind.Preview,
                "Preparing preview"));
        }
        if (snapshot.ThumbnailCount > 0)
        {
            activities.Add(new ActivityText(
                BackgroundActivityKind.Thumbnails,
                "Loading thumbnails"));
        }
        if (snapshot.MetadataCount > 0 &&
            snapshot.Export == null && snapshot.CaptureTimes == null)
        {
            activities.Add(new ActivityText(
                BackgroundActivityKind.Metadata,
                "Reading metadata"));
        }
        if (snapshot.CacheWriteCount > 0)
        {
            activities.Add(new ActivityText(
                BackgroundActivityKind.CacheWrites,
                $"Saving caches — {snapshot.CacheWriteCount:N0}"));
        }

        if (activities.Count == 0)
        {
            return BackgroundActivityDisplay.Hidden;
        }

        var primary = activities[0];
        var label = activities.Count == 1
            ? primary.Label
            : $"{primary.Label} +{activities.Count - 1}";
        var progress = primary.Kind switch
        {
            BackgroundActivityKind.Export when snapshot.Export is { } exportProgress =>
                new BackgroundProgress(exportProgress.Current, exportProgress.Total),
            BackgroundActivityKind.CaptureTimes when snapshot.CaptureTimes is { } burstProgress =>
                burstProgress,
            _ => null
        };
        return new BackgroundActivityDisplay(
            false,
            label,
            string.Join(Environment.NewLine, activities.Select(item => item.Label)),
            activities.Count,
            progress is { Total: > 0 },
            Math.Max(0, progress?.Processed ?? 0),
            Math.Max(1, progress?.Total ?? 1));
    }

    private sealed record ActivityText(
        BackgroundActivityKind Kind,
        string Label);
}

internal sealed class BackgroundExportActivityRegistry
{
    private readonly object _sync = new();
    private readonly Action _onFirstScope;
    private readonly Dictionary<long, ScopeState> _scopes = [];
    private long _nextId;
    private long _scopeStartCount;
    private int _current;
    private int _total;

    public BackgroundExportActivityRegistry(Action onFirstScope)
    {
        _onFirstScope = onFirstScope;
    }

    public BackgroundExportScope Begin(int total)
    {
        var id = Interlocked.Increment(ref _nextId);
        Interlocked.Increment(ref _scopeStartCount);
        var wake = false;
        lock (_sync)
        {
            wake = _scopes.Count == 0;
            var boundedTotal = Math.Max(0, total);
            _scopes.Add(id, new ScopeState(boundedTotal, 0));
            _total += boundedTotal;
        }
        if (wake) _onFirstScope();
        return new BackgroundExportScope(this, id, Math.Max(0, total));
    }

    internal long ScopeStartCount => Interlocked.Read(ref _scopeStartCount);

    public ExportActivitySnapshot? GetSnapshot()
    {
        lock (_sync)
        {
            return _scopes.Count == 0
                ? null
                : new ExportActivitySnapshot(_scopes.Count, _current, _total);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _scopes.Clear();
            _current = 0;
            _total = 0;
        }
    }

    private void Report(long id, int current)
    {
        lock (_sync)
        {
            if (!_scopes.TryGetValue(id, out var state)) return;
            var next = Math.Clamp(current, 0, state.Total);
            _current += next - state.Current;
            _scopes[id] = state with { Current = next };
        }
    }

    private void End(long id)
    {
        lock (_sync)
        {
            if (!_scopes.Remove(id, out var state)) return;
            _total = Math.Max(0, _total - state.Total);
            _current = Math.Max(0, _current - state.Current);
        }
    }

    private sealed record ScopeState(int Total, int Current);

    internal sealed class BackgroundExportScope : IDisposable
    {
        private BackgroundExportActivityRegistry? _owner;
        private readonly long _id;
        private readonly int _total;

        internal BackgroundExportScope(
            BackgroundExportActivityRegistry owner,
            long id,
            int total)
        {
            _owner = owner;
            _id = id;
            _total = total;
        }

        public void Report(int current)
        {
            var owner = Volatile.Read(ref _owner);
            if (owner == null) return;
            owner.Report(_id, Math.Clamp(current, 0, _total));
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner != null)
            {
                owner.End(_id);
            }
        }

        public override string ToString() => $"Export activity {_id}";
    }
}

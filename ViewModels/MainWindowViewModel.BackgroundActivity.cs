using Avalonia.Threading;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    internal static readonly TimeSpan BackgroundActivitySampleInterval =
        TimeSpan.FromMilliseconds(250);
    private readonly BackgroundActivityAggregator _backgroundActivityAggregator = new();
    private readonly object _directThumbnailActivitySync = new();
    private readonly HashSet<Task> _directThumbnailActivities =
        new(ReferenceEqualityComparer.Instance);
    private BackgroundExportActivityRegistry _exportActivities = null!;
    private DispatcherTimer? _backgroundActivityTimer;
    private BackgroundActivityDisplay _backgroundActivity =
        BackgroundActivityDisplay.Hidden;
    private long _backgroundActivityEpoch;
    private int _initialThumbnailBatches;
    private int _backgroundActivityDisposed;
    private int _backgroundActivityPumpCount;

    public BackgroundActivityDisplay BackgroundActivity
    {
        get => _backgroundActivity;
        private set => SetProperty(ref _backgroundActivity, value);
    }

    internal int InitialThumbnailBatchCount =>
        Volatile.Read(ref _initialThumbnailBatches);

    internal int DirectThumbnailActivityCount
    {
        get
        {
            lock (_directThumbnailActivitySync)
                return _directThumbnailActivities.Count;
        }
    }

    internal int SchedulerThumbnailActivityCount =>
        Volatile.Read(ref _thumbnailScheduler)?.DesiredCount ?? 0;

    internal long ExportActivityScopeStartCount =>
        _exportActivities.ScopeStartCount;

    internal bool IsBackgroundActivitySamplerRunning =>
        _backgroundActivityTimer?.IsEnabled == true;

    internal int BackgroundActivityPumpCount =>
        Volatile.Read(ref _backgroundActivityPumpCount);

    internal long BackgroundActivityEpoch =>
        Interlocked.Read(ref _backgroundActivityEpoch);

    internal void EnsureBackgroundActivitySamplerRunning()
    {
        if (Volatile.Read(ref _backgroundActivityDisposed) != 0)
        {
            return;
        }
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(EnsureBackgroundActivitySamplerRunning);
            return;
        }
        if (_backgroundActivityTimer?.IsEnabled == true)
        {
            return;
        }

        _backgroundActivityTimer = new DispatcherTimer
        {
            Interval = BackgroundActivitySampleInterval
        };
        _backgroundActivityTimer.Tick += OnBackgroundActivityTimerTick;
        _backgroundActivityTimer.Start();
        PumpBackgroundActivity();
    }

    internal void PumpBackgroundActivity(DateTimeOffset? now = null)
    {
        Interlocked.Increment(ref _backgroundActivityPumpCount);
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var epoch = BackgroundActivityEpoch;
        var snapshot = CaptureBackgroundActivitySnapshot();
        BackgroundActivity = _backgroundActivityAggregator.Aggregate(
            snapshot,
            timestamp,
            epoch);
        if (_backgroundActivityAggregator.CanStop(
            snapshot,
            timestamp,
            epoch))
        {
            StopBackgroundActivitySampler();
        }
    }

    internal BackgroundActivitySnapshot CaptureBackgroundActivitySnapshot()
    {
        var serviceThumbnails = 0;
        var previews = 0;
        var cacheWrites = 0;
        var metadata = 0;
        if (_imageService.IsValueCreated)
        {
            var service = _imageService.Value;
            serviceThumbnails = service.Previews.RenderedThumbnailTaskCount;
            previews = service.Previews.PreviewActivityCount;
            cacheWrites = service.CacheWriteActivityCount;
            metadata = service.Metadata.InFlightCount;
        }

        var scheduler = Volatile.Read(ref _thumbnailScheduler);
        var thumbnails = InitialThumbnailBatchCount +
            DirectThumbnailActivityCount +
            (scheduler?.DesiredCount ?? 0) +
            serviceThumbnails;
        var burst = Volatile.Read(ref _burstAnalysisActive) == 0
            ? null
            : new BackgroundProgress(
                Volatile.Read(ref _burstAnalysisProcessed),
                Volatile.Read(ref _burstAnalysisTotal));
        return new BackgroundActivitySnapshot(
            thumbnails,
            previews,
            cacheWrites,
            metadata,
            burst,
            _exportActivities.GetSnapshot());
    }

    internal IDisposable BeginInitialThumbnailBatch()
    {
        if (Interlocked.Increment(ref _initialThumbnailBatches) == 1)
        {
            SignalBackgroundActivityStarted();
        }
        return new ActivityRegistration(() =>
            Interlocked.Decrement(ref _initialThumbnailBatches));
    }

    internal Task TrackDirectThumbnailOperation(Task operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var wake = false;
        lock (_directThumbnailActivitySync)
        {
            wake = _directThumbnailActivities.Count == 0;
            _directThumbnailActivities.Add(operation);
        }
        if (wake) SignalBackgroundActivityStarted();
        _ = ObserveDirectThumbnailOperationAsync(operation);
        return operation;
    }

    internal BackgroundExportActivityRegistry.BackgroundExportScope
        BeginExportActivity(int total) => _exportActivities.Begin(total);

    internal void SignalBackgroundActivityStarted()
    {
        Interlocked.Increment(ref _backgroundActivityEpoch);
        EnsureBackgroundActivitySamplerRunning();
    }

    private async Task ObserveDirectThumbnailOperationAsync(Task operation)
    {
        try
        {
            await operation;
        }
        catch
        {
        }
        finally
        {
            lock (_directThumbnailActivitySync)
                _directThumbnailActivities.Remove(operation);
        }
    }

    internal void OnRenderedThumbnailWorkStarted()
    {
        SignalBackgroundActivityStarted();
    }

    private void OnBackgroundActivityTimerTick(object? sender, EventArgs e) =>
        PumpBackgroundActivity();

    private void StopBackgroundActivitySampler()
    {
        var timer = Interlocked.Exchange(ref _backgroundActivityTimer, null);
        if (timer == null) return;
        timer.Tick -= OnBackgroundActivityTimerTick;
        timer.Stop();
    }

    private void DisposeBackgroundActivity()
    {
        Interlocked.Exchange(ref _backgroundActivityDisposed, 1);
        StopBackgroundActivitySampler();
        BackgroundActivity = BackgroundActivityDisplay.Hidden;
    }

    private async Task DrainBackgroundActivityAsync()
    {
        Task[] operations;
        lock (_directThumbnailActivitySync)
            operations = _directThumbnailActivities.ToArray();
        if (operations.Length > 0)
        {
            var drain = Task.WhenAll(operations.Select(
                IgnoreActivityFailureAsync));
            await Task.WhenAny(
                drain,
                Task.Delay(TimeSpan.FromSeconds(2)));
        }
        lock (_directThumbnailActivitySync) _directThumbnailActivities.Clear();
        Volatile.Write(ref _initialThumbnailBatches, 0);
        _exportActivities.Clear();
    }

    private static async Task IgnoreActivityFailureAsync(Task operation)
    {
        try
        {
            await operation;
        }
        catch
        {
        }
    }

    private sealed class ActivityRegistration : IDisposable
    {
        private Action? _dispose;

        public ActivityRegistration(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

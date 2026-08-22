using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed record PreviewSourceAnalysis(
    HistogramData? RawHistogram,
    SourceSaturationMask? SourceSaturation)
{
    public static PreviewSourceAnalysis Empty { get; } = new(null, null);
}

internal sealed partial class PreviewBaseCoordinator
{
    private sealed record BaseIdentity(string Path, string DecodeKey);

    private sealed class HeldBase
    {
        private readonly object _sync = new();
        private BaseImage? _image;
        private int _leases;
        private bool _retired;

        public PreviewSourceAnalysis Analysis { get; }

        public HeldBase(
            BaseImage image,
            PreviewSourceAnalysis? analysis = null)
        {
            _image = image;
            Analysis = analysis ?? PreviewSourceAnalysis.Empty;
        }

        public PreviewBaseLease AcquireLease(
            Task<BaseImageLoadFailure>? refreshTask) =>
            new(Acquire(), Analysis, refreshTask);

        public PreviewBaseSnapshot Acquire()
        {
            lock (_sync)
            {
                if (_retired || _image == null)
                {
                    throw new ObjectDisposedException(nameof(HeldBase));
                }

                _leases++;
                return new PreviewBaseSnapshot(_image, Release);
            }
        }

        public void Retire()
        {
            BaseImage? dispose = null;
            lock (_sync)
            {
                _retired = true;
                if (_leases == 0)
                {
                    dispose = _image;
                    _image = null;
                }
            }
            dispose?.Dispose();
        }

        private void Release()
        {
            BaseImage? dispose = null;
            lock (_sync)
            {
                if (_leases <= 0)
                {
                    return;
                }

                _leases--;
                if (_retired && _leases == 0)
                {
                    dispose = _image;
                    _image = null;
                }
            }
            dispose?.Dispose();
        }
    }
}

internal sealed class PreviewBaseLease : IDisposable
{
    private PreviewBaseSnapshot? _snapshot;
    private readonly PreviewSourceAnalysis _analysis;

    public BaseImage Base =>
        _snapshot?.Base ??
        throw new ObjectDisposedException(nameof(PreviewBaseLease));

    public PreviewSourceAnalysis Analysis
    {
        get
        {
            ObjectDisposedException.ThrowIf(_snapshot == null, this);
            return _analysis;
        }
    }

    public Task<BaseImageLoadFailure>? RefreshTask { get; }

    public bool IsStale => RefreshTask != null;

    public PreviewBaseLease(
        PreviewBaseSnapshot snapshot,
        PreviewSourceAnalysis analysis,
        Task<BaseImageLoadFailure>? refreshTask)
    {
        _snapshot = snapshot;
        _analysis = analysis;
        RefreshTask = refreshTask;
    }

    public void Dispose() =>
        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
}

internal sealed class PreviewBaseSnapshot : IDisposable
{
    private BaseImage? _base;
    private Action? _release;

    public BaseImage Base =>
        _base ?? throw new ObjectDisposedException(nameof(PreviewBaseSnapshot));

    public PreviewBaseSnapshot(BaseImage image, Action release)
    {
        _base = image;
        _release = release;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _base, null);
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

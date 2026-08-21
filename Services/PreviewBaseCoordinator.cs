using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class PreviewBaseCoordinator : IAsyncDisposable
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private readonly IBaseImageLoader _loader;
    private readonly object _sync = new();
    private readonly HashSet<Task> _decodeTasks = [];
    private HeldBase? _heldInteractiveBase;
    private HeldBase? _heldLargeBase;
    private BaseIdentity? _heldIdentity;
    private DecodeSession? _currentDecode;
    private long _generation;
    private bool _disposed;

    public PreviewBaseCoordinator(IBaseImageLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public int DecodeTaskCount
    {
        get
        {
            lock (_sync) return _decodeTasks.Count;
        }
    }

    public async Task<PreviewBaseAcquisition?> GetPreviewAsync(
        ImageFile imageFile,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        (await GetPreviewResultAsync(
            imageFile,
            decode,
            cancellationToken).ConfigureAwait(false)).Acquisition;

    internal async Task<PreviewBaseResult> GetPreviewResultAsync(
        ImageFile imageFile,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageFile);
        ArgumentNullException.ThrowIfNull(decode);
        if (decode.ProfileSelection != null && decode.ProfileResolution == null)
        {
            // Bases are keyed by the selection token; decoding without the
            // resolved profile would install a profile-less base under the
            // resolved key and poison every later render from it.
            throw new ArgumentException(
                "A profile-selecting decode must carry its resolution before " +
                "it can initiate a base decode.",
                nameof(decode));
        }
        cancellationToken.ThrowIfCancellationRequested();

        var identity = new BaseIdentity(
            Path.GetFullPath(imageFile.FilePath),
            decode.CacheKey);
        Task<BaseImageLoadFailure> decodeTask;

        lock (_sync)
        {
            ThrowIfDisposed();
            if (Matches(_heldIdentity, identity))
            {
                return PreviewBaseResult.Loaded(new PreviewBaseAcquisition(
                    AcquireHeldInteractiveBase(),
                    null));
            }

            if (_currentDecode is { } current &&
                Matches(current.Identity, identity))
            {
                decodeTask = current.Task;
            }
            else
            {
                RetireForReplacement(identity);
                decodeTask = StartDecode(imageFile, decode, identity);
            }

            if (_heldInteractiveBase != null &&
                SamePath(_heldIdentity, identity))
            {
                return PreviewBaseResult.Loaded(new PreviewBaseAcquisition(
                    AcquireHeldInteractiveBase(),
                    decodeTask));
            }
        }

        var failure = await decodeTask.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            ThrowIfDisposed();
            return Matches(_heldIdentity, identity)
                ? PreviewBaseResult.Loaded(new PreviewBaseAcquisition(
                    AcquireHeldInteractiveBase(), null))
                : PreviewBaseResult.Failed(failure);
        }
    }

    public PreviewBaseAcquisition? TryAcquireCurrent(
        ImageFile imageFile,
        BaseDecodeSettings decode)
    {
        ArgumentNullException.ThrowIfNull(imageFile);
        ArgumentNullException.ThrowIfNull(decode);
        var identity = new BaseIdentity(
            Path.GetFullPath(imageFile.FilePath),
            decode.CacheKey);

        lock (_sync)
        {
            ThrowIfDisposed();
            return Matches(_heldIdentity, identity)
                ? new PreviewBaseAcquisition(
                    AcquireHeldInteractiveBase(), null)
                : null;
        }
    }

    public PreviewBaseSnapshot? TryAcquireLargeCurrent(
        ImageFile imageFile,
        BaseDecodeSettings decode)
    {
        ArgumentNullException.ThrowIfNull(imageFile);
        ArgumentNullException.ThrowIfNull(decode);
        var identity = new BaseIdentity(
            Path.GetFullPath(imageFile.FilePath),
            decode.CacheKey);

        lock (_sync)
        {
            ThrowIfDisposed();
            return Matches(_heldIdentity, identity) && _heldLargeBase != null
                ? _heldLargeBase.Acquire()
                : null;
        }
    }

    public void Clear()
    {
        HeldBase? interactive;
        HeldBase? large;
        lock (_sync)
        {
            _generation++;
            _currentDecode?.Cancellation.Cancel();
            _currentDecode = null;
            interactive = _heldInteractiveBase;
            large = _heldLargeBase;
            _heldInteractiveBase = null;
            _heldLargeBase = null;
            _heldIdentity = null;
        }
        large?.Retire();
        interactive?.Retire();
    }

    private Task<BaseImageLoadFailure> StartDecode(
        ImageFile imageFile,
        BaseDecodeSettings decode,
        BaseIdentity identity)
    {
        _currentDecode?.Cancellation.Cancel();
        var session = new DecodeSession(identity, ++_generation);
        _currentDecode = session;
        session.Task = Task.Run(
            () => DecodeAndInstall(imageFile, decode, session),
            CancellationToken.None);
        _decodeTasks.Add(session.Task);
        _ = session.Task.ContinueWith(
            completed =>
            {
                lock (_sync)
                {
                    _decodeTasks.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return session.Task;
    }

    private BaseImageLoadFailure DecodeAndInstall(
        ImageFile imageFile,
        BaseDecodeSettings decode,
        DecodeSession session)
    {
        PreviewBasePair? decoded = null;
        HeldBase? supersededInteractive = null;
        HeldBase? supersededLarge = null;
        var failure = BaseImageLoadFailure.DecodeFailed;
        try
        {
            var outcome = _loader.LoadPreviewBaseWithOutcome(
                imageFile,
                decode,
                session.Cancellation.Token);
            decoded = outcome.Pair;
            failure = outcome.Failure;
            session.Cancellation.Token.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (!_disposed &&
                    ReferenceEquals(_currentDecode, session) &&
                    session.Generation == _generation)
                {
                    if (decoded != null)
                    {
                        var interactive = decoded.DetachInteractive();
                        var large = decoded.DetachLarge();
                        supersededInteractive = _heldInteractiveBase;
                        supersededLarge = _heldLargeBase;
                        _heldInteractiveBase = new HeldBase(interactive);
                        _heldLargeBase = large == null
                            ? null
                            : new HeldBase(large);
                        _heldIdentity = session.Identity;
                        ImageServiceHelpers.LogDisplayTrace(
                            $"base installed key={session.Identity.DecodeKey} " +
                            $"profile={interactive.Info.DcpProfile?.Name ?? "NONE"} " +
                            $"status={interactive.Info.ProfileStatus} " +
                            $"bias={interactive.Info.SourceExposureBiasEv:F4} " +
                            $"hasLarge={large != null}");
                    }
                    _currentDecode = null;
                }
            }
            return failure;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_currentDecode, session))
                {
                    _currentDecode = null;
                }
            }
            decoded?.Dispose();
            supersededLarge?.Retire();
            supersededInteractive?.Retire();
            session.Cancellation.Dispose();
        }
    }

    private PreviewBaseSnapshot AcquireHeldInteractiveBase()
    {
        var held = _heldInteractiveBase ??
            throw new InvalidOperationException("The held preview base is missing.");
        return held.Acquire();
    }

    private void RetireForReplacement(BaseIdentity identity)
    {
        if (_heldIdentity == null)
        {
            return;
        }

        _heldLargeBase?.Retire();
        _heldLargeBase = null;
        if (SamePath(_heldIdentity, identity))
        {
            return;
        }

        _heldInteractiveBase?.Retire();
        _heldInteractiveBase = null;
        _heldIdentity = null;
    }

    private static bool Matches(BaseIdentity? left, BaseIdentity right) =>
        left != null &&
        PathComparer.Equals(left.Path, right.Path) &&
        string.Equals(left.DecodeKey, right.DecodeKey, StringComparison.Ordinal);

    private static bool SamePath(BaseIdentity? left, BaseIdentity right) =>
        left != null && PathComparer.Equals(left.Path, right.Path);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _generation++;
            _currentDecode?.Cancellation.Cancel();
            _currentDecode = null;
            _heldLargeBase?.Retire();
            _heldInteractiveBase?.Retire();
            _heldLargeBase = null;
            _heldInteractiveBase = null;
            _heldIdentity = null;
            pending = [.. _decodeTasks];
        }

        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ImageServiceHelpers.LogError(
                $"Preview decode failed during shutdown: {ex.Message}");
        }
    }

    private sealed record BaseIdentity(string Path, string DecodeKey);

    private sealed class HeldBase
    {
        private readonly object _sync = new();
        private BaseImage? _image;
        private int _leases;
        private bool _retired;

        public HeldBase(BaseImage image)
        {
            _image = image;
        }

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

    private sealed class DecodeSession
    {
        public BaseIdentity Identity { get; }
        public long Generation { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public Task<BaseImageLoadFailure> Task { get; set; } =
            System.Threading.Tasks.Task.FromResult(
                BaseImageLoadFailure.DecodeFailed);

        public DecodeSession(BaseIdentity identity, long generation)
        {
            Identity = identity;
            Generation = generation;
        }
    }
}

internal sealed class PreviewBaseAcquisition : IDisposable
{
    private PreviewBaseSnapshot? _snapshot;

    public BaseImage Base =>
        _snapshot?.Base ??
        throw new ObjectDisposedException(nameof(PreviewBaseAcquisition));

    public Task<BaseImageLoadFailure>? RefreshTask { get; }

    public bool IsStale => RefreshTask != null;

    public PreviewBaseAcquisition(
        PreviewBaseSnapshot snapshot,
        Task<BaseImageLoadFailure>? refreshTask)
    {
        _snapshot = snapshot;
        RefreshTask = refreshTask;
    }

    public void Dispose() =>
        Interlocked.Exchange(ref _snapshot, null)?.Dispose();
}

internal sealed record PreviewBaseResult(
    PreviewBaseAcquisition? Acquisition,
    BaseImageLoadFailure Failure)
{
    public static PreviewBaseResult Loaded(PreviewBaseAcquisition acquisition) =>
        new(acquisition, BaseImageLoadFailure.None);

    public static PreviewBaseResult Failed(BaseImageLoadFailure failure) =>
        new(null, failure);
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

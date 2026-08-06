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
    private HeldBase? _heldBase;
    private BaseIdentity? _heldIdentity;
    private DecodeSession? _currentDecode;
    private long _generation;
    private bool _disposed;

    public PreviewBaseCoordinator(IBaseImageLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public async Task<PreviewBaseAcquisition?> GetPreviewAsync(
        ImageFile imageFile,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imageFile);
        ArgumentNullException.ThrowIfNull(decode);
        cancellationToken.ThrowIfCancellationRequested();

        var identity = new BaseIdentity(
            Path.GetFullPath(imageFile.FilePath),
            decode.CacheKey);
        Task decodeTask;

        lock (_sync)
        {
            ThrowIfDisposed();
            if (Matches(_heldIdentity, identity))
            {
                return new PreviewBaseAcquisition(
                    AcquireHeldBase(),
                    null);
            }

            if (_currentDecode is { } current &&
                Matches(current.Identity, identity))
            {
                decodeTask = current.Task;
            }
            else
            {
                decodeTask = StartDecode(imageFile, decode, identity);
            }

            if (_heldBase != null &&
                SamePath(_heldIdentity, identity))
            {
                return new PreviewBaseAcquisition(
                    AcquireHeldBase(),
                    decodeTask);
            }
        }

        await decodeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            ThrowIfDisposed();
            return Matches(_heldIdentity, identity)
                ? new PreviewBaseAcquisition(AcquireHeldBase(), null)
                : null;
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
                ? new PreviewBaseAcquisition(AcquireHeldBase(), null)
                : null;
        }
    }

    public void Clear()
    {
        HeldBase? held;
        lock (_sync)
        {
            _generation++;
            _currentDecode?.Cancellation.Cancel();
            _currentDecode = null;
            held = _heldBase;
            _heldBase = null;
            _heldIdentity = null;
        }
        held?.Retire();
    }

    private Task StartDecode(
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

    private void DecodeAndInstall(
        ImageFile imageFile,
        BaseDecodeSettings decode,
        DecodeSession session)
    {
        BaseImage? decoded = null;
        HeldBase? superseded = null;
        try
        {
            decoded = _loader.LoadPreviewBase(
                imageFile,
                decode,
                session.Cancellation.Token);
            session.Cancellation.Token.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (!_disposed &&
                    ReferenceEquals(_currentDecode, session) &&
                    session.Generation == _generation)
                {
                    if (decoded != null)
                    {
                        superseded = _heldBase;
                        _heldBase = new HeldBase(decoded);
                        _heldIdentity = session.Identity;
                        decoded = null;
                    }
                    _currentDecode = null;
                }
            }
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
            superseded?.Retire();
            session.Cancellation.Dispose();
        }
    }

    private PreviewBaseSnapshot AcquireHeldBase()
    {
        var held = _heldBase ??
            throw new InvalidOperationException("The held preview base is missing.");
        return held.Acquire();
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
            _heldBase?.Retire();
            _heldBase = null;
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
        public Task Task { get; set; } = Task.CompletedTask;

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

    public Task? RefreshTask { get; }

    public bool IsStale => RefreshTask != null;

    public PreviewBaseAcquisition(
        PreviewBaseSnapshot snapshot,
        Task? refreshTask)
    {
        _snapshot = snapshot;
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

using System.Xml.Linq;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class XmpSidecarWriter : IAsyncDisposable
{
    private const int DefaultCapacity = 512;
    private readonly CatalogService _catalog;
    private readonly ISourceAvailabilityService _availability;
    private readonly TryReadExifOrientation _readOrientation;
    private readonly IReadOnlyDictionary<ColorLabel, string> _labelNames;
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, WriteJob> _jobs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _ready = new();
    private readonly HashSet<string> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _signal = new(0);
    private TaskCompletionSource _idle = CompletedIdle();
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private bool _accepting;

    public Action<string>? Report { get; set; }
    internal Func<string, CancellationToken, Task>? BeforePromotionAsync { get; set; }

    public XmpSidecarWriter(
        CatalogService catalog,
        IReadOnlyDictionary<ColorLabel, string> labelNames,
        int capacity = DefaultCapacity)
        : this(catalog, labelNames, new SourceAvailabilityService(), capacity,
            ImageServiceHelpers.TryGetExifOrientation)
    {
    }

    internal XmpSidecarWriter(
        CatalogService catalog,
        IReadOnlyDictionary<ColorLabel, string> labelNames,
        ISourceAvailabilityService availability,
        int capacity = DefaultCapacity,
        TryReadExifOrientation? readOrientation = null)
    {
        _catalog = catalog;
        _labelNames = labelNames;
        _availability = availability;
        _readOrientation = readOrientation ??
            ImageServiceHelpers.TryGetExifOrientation;
        _capacity = Math.Max(1, capacity);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_accepting) return;
            _lifetime = new CancellationTokenSource();
            _accepting = true;
            _worker = Task.Run(() => RunAsync(_lifetime.Token));
        }
    }

    public bool TryEnqueue(
        AssessmentSnapshot snapshot,
        AssessmentAxes axes,
        IReadOnlyCollection<string> folderImagePaths,
        XmpSidecarNaming naming)
    {
        // An associated target is unique per image (ambiguous base names are
        // excluded), so the image path is a stable, I/O-free queue key.
        var key = snapshot.FilePath;
        lock (_gate)
        {
            if (!_accepting) return false;
            if (_jobs.TryGetValue(key, out var existing))
            {
                _jobs[key] = existing with
                {
                    Snapshot = snapshot.Revision >= existing.Snapshot.Revision
                        ? snapshot
                        : existing.Snapshot,
                    Axes = existing.Axes | axes
                };
                return true;
            }
            if (!_active.Contains(key) && _jobs.Count + _active.Count >= _capacity)
            {
                Report?.Invoke($"XMP queue full; write pending for {snapshot.FilePath}");
                return false;
            }
            if (_idle.Task.IsCompleted)
                _idle = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            _jobs.Add(key, new WriteJob(
                key, snapshot, axes, folderImagePaths.ToArray(), naming));
            _ready.Enqueue(key);
            _signal.Release();
            return true;
        }
    }

    internal Task DrainAsync()
    {
        lock (_gate) return _idle.Task;
    }

    public async Task StopAsync()
    {
        Task? worker;
        lock (_gate)
        {
            if (!_accepting && _worker == null) return;
            _accepting = false;
            foreach (var job in _jobs.Values)
                Report?.Invoke($"XMP write left pending for {job.Snapshot.FilePath}");
            _jobs.Clear();
            _ready.Clear();
            if (_active.Count == 0) _idle.TrySetResult();
            _lifetime?.Cancel();
            _signal.Release();
            worker = _worker;
        }
        if (worker != null)
        {
            try { await worker; }
            catch (OperationCanceledException) { }
        }
        lock (_gate)
        {
            _lifetime?.Dispose();
            _lifetime = null;
            _worker = null;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _signal.WaitAsync(cancellationToken);
            WriteJob? job = null;
            lock (_gate)
            {
                while (_ready.Count > 0 && job == null)
                {
                    var key = _ready.Dequeue();
                    if (_jobs.Remove(key, out var found))
                    {
                        job = found;
                        _active.Add(key);
                    }
                }
                if (job == null && !_accepting) return;
            }
            if (job == null) continue;
            try
            {
                await ProcessAsync(job, cancellationToken);
            }
            finally
            {
                lock (_gate)
                {
                    _active.Remove(job.QueueKey);
                    if (_jobs.Count == 0 && _active.Count == 0)
                        _idle.TrySetResult();
                }
            }
        }
    }

    private async Task ProcessAsync(WriteJob job, CancellationToken cancellationToken)
    {
        try
        {
            var result = await TryWriteAsync(job, cancellationToken);
            if (!result.Succeeded)
            {
                Report?.Invoke($"XMP write failed for {job.Snapshot.FilePath}");
                return;
            }
            if (result.ResolvedAxes == AssessmentAxes.None) return;

            if (await _catalog.ClearPendingAxesAsync(
                    job.Snapshot.ImageId, job.Snapshot.Revision,
                    result.ResolvedAxes, cancellationToken))
            {
                return;
            }

            var current = (await _catalog.LoadAssessmentSnapshotsAsync(
                [job.Snapshot.ImageId], cancellationToken)).Single();
            var unresolved = current.PendingAxes & result.ResolvedAxes;
            if (unresolved != AssessmentAxes.None &&
                !TryEnqueue(current, unresolved, job.FolderImagePaths, job.Naming))
            {
                Report?.Invoke($"XMP write superseded and left pending for {current.FilePath}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Report?.Invoke($"XMP write canceled for {job.Snapshot.FilePath}");
        }
        catch (Exception exception)
        {
            Report?.Invoke(
                $"XMP write failed for {job.Snapshot.FilePath}: {exception.Message}");
        }
    }

    private async Task<WriteResult> TryWriteAsync(
        WriteJob job,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            XmpCropProjection? cropProjection = null;
            string? cropSkipReason = null;
            var resolvedAxes = job.Axes;
            if (job.Axes.HasFlag(AssessmentAxes.Crop))
            {
                var crop = await LoadCropProjectionAsync(job, cancellationToken);
                cropProjection = crop.Projection;
                cropSkipReason = crop.SkipReason;
                if (crop.RetryPending)
                    resolvedAxes &= ~AssessmentAxes.Crop;
            }

            var mergeAxes = job.Axes;
            if (!resolvedAxes.HasFlag(AssessmentAxes.Crop))
                mergeAxes &= ~AssessmentAxes.Crop;
            if (mergeAxes == AssessmentAxes.None)
            {
                ReportCropOutcome(job, cropProjection, cropSkipReason, default);
                return new(true, resolvedAxes);
            }

            var before = XmpSidecarPaths.Resolve(
                job.Snapshot.FilePath, job.FolderImagePaths, job.Naming);
            if (before.Shadowed != null)
                Report?.Invoke($"Shadowed XMP sidecar left untouched: {before.Shadowed.Path}");
            var target = before.Winner?.Path ?? before.CreationPath;
            if (!CanAccessTarget(target, before.Winner != null)) return default;

            XDocument document;
            try
            {
                document = before.Winner == null
                    ? XmpSidecarDocument.Create()
                    : await LoadAsync(target, cancellationToken);
            }
            catch (IOException exception) when (
                exception is not XmpSidecarTooLargeException && attempt < 2)
            {
                continue;
            }
            catch (UnauthorizedAccessException) when (attempt < 2) { continue; }

            var merge = XmpSidecarDocument.Merge(
                document, job.Snapshot,
                mergeAxes,
                _labelNames, cropProjection);
            if (!merge.Changed)
            {
                ReportCropOutcome(job, cropProjection, cropSkipReason, merge);
                return new(true, resolvedAxes);
            }
            if (before.Winner == null)
            {
                var bootstrap = XmpSidecarDocument.Merge(
                    document, job.Snapshot,
                    AssessmentAxes.Rating | AssessmentAxes.Flag |
                    AssessmentAxes.Label,
                    _labelNames);
                merge = merge with
                {
                    ReplacedUnsupportedLabel = merge.ReplacedUnsupportedLabel ||
                                               bootstrap.ReplacedUnsupportedLabel,
                    Changed = true
                };
            }
            var temporary = Path.Combine(
                Path.GetDirectoryName(target)!,
                $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await SaveAsync(document, temporary, cancellationToken);
                if (BeforePromotionAsync != null)
                    await BeforePromotionAsync(target, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var after = XmpSidecarPaths.Resolve(
                    job.Snapshot.FilePath, job.FolderImagePaths, job.Naming);
                if (!SameResolution(before, after)) continue;
                File.Move(temporary, target, overwrite: true);
                ReportMergeOutcome(job, cropProjection, cropSkipReason, merge);
                return new(true, resolvedAxes);
            }
            catch (IOException) when (attempt < 2) { }
            catch (UnauthorizedAccessException) when (attempt < 2) { }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }
        return default;
    }

    private async Task<CropProjectionResult>
        LoadCropProjectionAsync(
        WriteJob job,
        CancellationToken cancellationToken)
    {
        var projection = await _catalog.LoadCropProjectionAsync(
            job.Snapshot.ImageId, cancellationToken);
        if (projection.Kind != XmpCropProjectionKind.Portable)
            return new(projection, null, false);
        if (!SourceAccessPolicy.CanRead(
                _availability.GetAvailability(job.Snapshot.FilePath),
                SourceReadIntent.Background))
        {
            return new(projection, "source orientation is unavailable", true);
        }
        if (!_readOrientation(job.Snapshot.FilePath, out var orientation))
            return new(projection, "source orientation could not be read", true);
        return orientation is 0 or 1
            ? new(projection, null, false)
            : new(new(XmpCropProjectionKind.NotPortable, null,
                "source orientation is not 1"), null, false);
    }

    private void ReportMergeOutcome(
        WriteJob job,
        XmpCropProjection? cropProjection,
        string? cropSkipReason,
        XmpMergeResult merge)
    {
        if (merge.ReplacedUnsupportedLabel)
            Report?.Invoke(
                $"Unsupported XMP label replaced for {job.Snapshot.FilePath}");
        if (merge.ReplacedUnsupportedCrop)
            Report?.Invoke(
                $"Unsupported XMP crop replaced for {job.Snapshot.FilePath}");
        ReportCropOutcome(job, cropProjection, cropSkipReason, merge);
    }

    private void ReportCropOutcome(
        WriteJob job,
        XmpCropProjection? cropProjection,
        string? cropSkipReason,
        XmpMergeResult merge)
    {
        if (cropSkipReason == null &&
            cropProjection?.Kind != XmpCropProjectionKind.NotPortable &&
            !merge.SkippedCrop)
        {
            return;
        }
        Report?.Invoke(
            $"XMP crop skipped for {job.Snapshot.FilePath}: " +
            (cropSkipReason ?? merge.CropSkipReason ?? cropProjection?.Reason));
    }

    private bool CanAccessTarget(string path, bool exists)
    {
        var probe = exists ? path : Path.GetDirectoryName(path)!;
        return SourceAccessPolicy.CanRead(
            _availability.GetAvailability(probe), SourceReadIntent.Background);
    }

    private static bool SameResolution(
        XmpSidecarResolution before,
        XmpSidecarResolution after) =>
        SameCandidate(before.Winner, after.Winner) &&
        SameCandidate(before.Shadowed, after.Shadowed) &&
        string.Equals(before.CreationPath, after.CreationPath,
            StringComparison.OrdinalIgnoreCase);

    private static bool SameCandidate(
        XmpSidecarCandidate? left,
        XmpSidecarCandidate? right) =>
        left == null && right == null ||
        left != null && right != null &&
        string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase) &&
        left.LastWriteUtc == right.LastWriteUtc && left.Length == right.Length;

    private static async Task<XDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        XmpSidecarReadStream.ThrowIfOversized(path);
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var bounded = new XmpSidecarReadStream(
            stream, path, XmpSidecarReader.MaximumSidecarBytes);
        return await XDocument.LoadAsync(
            bounded, LoadOptions.PreserveWhitespace, cancellationToken);
    }

    private static async Task SaveAsync(
        XDocument document,
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await document.SaveAsync(stream, SaveOptions.DisableFormatting, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _signal.Dispose();
    }

    private static TaskCompletionSource CompletedIdle()
    {
        var source = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private sealed record WriteJob(
        string QueueKey,
        AssessmentSnapshot Snapshot,
        AssessmentAxes Axes,
        IReadOnlyCollection<string> FolderImagePaths,
        XmpSidecarNaming Naming);

    private readonly record struct WriteResult(
        bool Succeeded,
        AssessmentAxes ResolvedAxes);

    private readonly record struct CropProjectionResult(
        XmpCropProjection Projection,
        string? SkipReason,
        bool RetryPending);
}

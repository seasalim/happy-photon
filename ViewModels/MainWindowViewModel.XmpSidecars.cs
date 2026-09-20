using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _xmpReconcileCts;
    private Task? _xmpReconcileTask;
    private XmpSidecarWriter? _xmpWriter;
    private IReadOnlyList<string> _xmpIndexedSidecars = [];
    private XmpSidecarMode _appliedXmpMode;
    private readonly HashSet<string> _inFlightDeletePaths =
        new(StringComparer.OrdinalIgnoreCase);

    [RelayCommand]
    private async Task WriteXmpSidecarsAsync()
    {
        var writer = _xmpWriter;
        if (!IsXmpReadWrite || writer == null) return;
        var targets = ResolveActionTargets().Targets.Where(image => image.Version == 1).ToArray();
        var generation = Volatile.Read(ref _browseGeneration);
        bool IsCurrent() => generation == Volatile.Read(ref _browseGeneration);
        Dictionary<long, ImageFile> livePrimaryRows = [];
        void RefreshLiveRows() => livePrimaryRows = Browse.AllImages
            .Where(image => image.Version == 1 && image.CatalogId != 0)
            .ToDictionary(image => image.CatalogId);
        bool IsLive(long id, string path) => !IsDeleteTargetClaimed(path) &&
            livePrimaryRows.ContainsKey(id);
        try
        {
            if (_xmpReconcileTask is { } reconcile)
            {
                try { await reconcile; }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The reconcile observer reports failures; catalog state remains publishable.
                }
            }
            if (!IsCurrent() || !IsXmpReadWrite) return;
            RefreshLiveRows();
            var ids = targets.Where(image => IsLive(image.CatalogId, image.FilePath))
                .Select(image => image.CatalogId).Where(id => id != 0).ToArray();
            // SQLite's async APIs still perform synchronous work; keep bulk calls off the UI thread.
            var marked = await Task.Run(() => _catalogService.MarkXmpPublicationPendingAsync(ids));
            if (!IsCurrent() || !IsXmpReadWrite) return;
            if (marked.Count == 0)
            {
                ShowTransientStatus("No XMP sidecars to write");
                return;
            }
            RefreshLiveRows();
            var paths = Browse.AllImages.Where(image => image.Version == 1)
                .Select(image => image.FilePath).ToArray();
            foreach (var (snapshot, _) in marked)
            {
                if (livePrimaryRows.TryGetValue(snapshot.ImageId, out var image))
                    ApplyAssessmentSnapshot(image, snapshot);
            }
            var current = marked.ToDictionary(row => row.Snapshot.ImageId, row => row.Snapshot);
            foreach (var (snapshot, axes) in marked)
            {
                while (!writer.CanAdmitPublication(out var stopped))
                {
                    if (stopped) break;
                    await writer.DrainAsync();
                    if (!IsCurrent() || !IsXmpReadWrite) return;
                    RefreshLiveRows();
                    // A mutation may have completed for a later target during the drain.
                    var liveIds = marked.Where(row => IsLive(row.Snapshot.ImageId, row.Snapshot.FilePath))
                        .Select(row => row.Snapshot.ImageId).ToArray();
                    current = (await Task.Run(() => _catalogService.LoadAssessmentSnapshotsAsync(liveIds)))
                        .ToDictionary(row => row.ImageId);
                    if (!IsCurrent() || !IsXmpReadWrite) return;
                    RefreshLiveRows();
                }
                if (!IsXmpReadWrite || !writer.CanAdmitPublication(out _)) break;
                if (!IsLive(snapshot.ImageId, snapshot.FilePath)) continue;
                if (current.TryGetValue(snapshot.ImageId, out var latest))
                    writer.TryEnqueue(latest, axes, paths, XmpSidecarNaming);
            }
            await writer.DrainAsync();
            if (!IsCurrent() || !IsXmpReadWrite) return;
            RefreshLiveRows();
            var remaining = marked.Where(row => IsLive(row.Snapshot.ImageId, row.Snapshot.FilePath))
                .ToDictionary(row => row.Snapshot.ImageId, row => row.Axes);
            var remainingIds = remaining.Keys.ToArray();
            var refreshed = await Task.Run(() => _catalogService.LoadAssessmentSnapshotsAsync(remainingIds));
            if (!IsCurrent() || !IsXmpReadWrite) return;
            RefreshLiveRows();
            foreach (var snapshot in refreshed)
            {
                if (livePrimaryRows.TryGetValue(snapshot.ImageId, out var image))
                    ApplyAssessmentSnapshot(image, snapshot);
            }
            var written = refreshed.Count(row => (row.PendingAxes & remaining[row.ImageId]) == 0);
            var pending = refreshed.Count - written;
            var message = $"XMP sidecars written for {written} " + (written == 1 ? "photo" : "photos");
            if (pending > 0)
                message += $"; XMP writes remain pending for {pending} " +
                    (pending == 1 ? "photo" : "photos");
            ShowTransientStatus(message);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"XMP publication failed: {exception.Message}");
            if (IsCurrent()) ShowTransientStatus("Unable to write XMP sidecars");
        }
    }

    private async Task ApplyXmpModeTransitionAsync(
        XmpSidecarMode newMode)
    {
        var oldMode = _appliedXmpMode;
        if (oldMode == newMode) return;
        if (oldMode == XmpSidecarMode.ReadWrite &&
            newMode != XmpSidecarMode.ReadWrite && _xmpWriter != null)
        {
            await _xmpWriter.StopAsync();
        }
        if (newMode == XmpSidecarMode.Off)
        {
            await CancelXmpReconcileAsync();
            _appliedXmpMode = newMode;
            return;
        }
        if (newMode == XmpSidecarMode.ReadWrite)
        {
            _xmpWriter ??= CreateXmpWriter();
            _xmpWriter.Start();
        }
        if (oldMode == XmpSidecarMode.Off && CurrentFolderPath != null)
            await StartXmpReconcileAsync(Volatile.Read(ref _browseGeneration));
        _appliedXmpMode = newMode;
    }

    private XmpSidecarWriter CreateXmpWriter()
    {
        var writer = new XmpSidecarWriter(_catalogService, _colorLabelNames);
        writer.Report = message => Dispatcher.UIThread.Post(() =>
        {
            System.Diagnostics.Debug.WriteLine($"[HappyPhoton] {message}");
            ShowTransientStatus(message);
        });
        return writer;
    }

    private async Task StartXmpReconcileAsync(int generation)
    {
        await CancelXmpReconcileAsync();
        if (XmpSidecarMode == XmpSidecarMode.Off ||
            Browse.AllImages.Count == 0 ||
            generation != Volatile.Read(ref _browseGeneration))
        {
            return;
        }
        var cts = new CancellationTokenSource();
        _xmpReconcileCts = cts;
        var paths = Browse.AllImages.Where(image => image.Version == 1)
            .Select(image => image.FilePath).ToArray();
        _xmpReconcileTask = Task.Run(async () =>
        {
            var reconciler = new XmpSidecarReconciler(_catalogService);
            var result = await reconciler.ReconcileAsync(
                paths, _colorLabelNames, XmpSidecarNaming,
                _xmpIndexedSidecars, cts.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
                ApplyXmpAdoptions(result, generation, cts));
        }, cts.Token);
        _ = ObserveXmpReconcileAsync(_xmpReconcileTask, cts);
    }

    private void ApplyXmpAdoptions(
        XmpReconcileResult result,
        int generation,
        CancellationTokenSource owner)
    {
        if (owner.IsCancellationRequested ||
            generation != Volatile.Read(ref _browseGeneration) ||
            !ReferenceEquals(_xmpReconcileCts, owner))
        {
            return;
        }
        var byPath = Browse.AllImages.Where(image => image.Version == 1).ToDictionary(
            image => image.FilePath, StringComparer.OrdinalIgnoreCase);
        var cropRefreshes = new List<ImageFile>();
        foreach (var adoption in result.Adoptions)
        {
            if (!byPath.TryGetValue(adoption.Snapshot.FilePath, out var image) ||
                image.AssessmentRevision + 1 != adoption.Snapshot.Revision)
            {
                continue;
            }
            var appliedCrop = ApplyXmpAdoption(image, adoption);
            if (appliedCrop) cropRefreshes.Add(image);
        }
        RefreshAdoptedCrops(cropRefreshes);
        if (result.Adoptions.Count > 0) Browse.RefreshFilters();
        ReportXmpReconcileIssues(result.Reports);
    }

    internal static bool ApplyXmpAdoption(
        ImageFile image,
        XmpReconcileAdoption adoption)
    {
        if (image.CatalogId == 0)
            image.CatalogId = adoption.Snapshot.ImageId;
        var appliedCrop = false;
        if (adoption.AdoptedAxes.HasFlag(AssessmentAxes.Crop) &&
            adoption.AdoptedCrop != null &&
            !XmpCropProjection.HasGeometryEdits(image.EditSettings))
        {
            image.EditSettings.Crop = adoption.AdoptedCrop.Clone();
            image.HasEdits = image.EditSettings.HasEdits;
            appliedCrop = true;
        }
        ApplyAssessmentSnapshot(
            image,
            adoption.Snapshot,
            adoption.AdoptedAxes);
        return appliedCrop;
    }

    private void RefreshAdoptedCrops(IReadOnlyList<ImageFile> images)
    {
        if (images.Count == 0) return;
        if (SelectedImage is { } selected && images.Contains(selected) &&
            selected.EditSettings.Crop != null)
        {
            CurrentCrop = selected.EditSettings.Crop.Clone();
            BeginDevelopHistoryLoad(IsDevelopMode ? selected : null);
            if (IsDevelopMode)
            {
                var renderGeneration = RequestEditedRender();
                TrackPreviewDebounce(UpdatePreviewWithCurrentSliders(
                    generation: renderGeneration));
            }
        }
        _ = TrackDirectThumbnailOperation(RefreshThumbnailsAsync(images));
    }

    internal void ReportXmpReconcileIssues(IReadOnlyList<string> reports)
    {
        if (reports.Count == 0) return;
        foreach (var report in reports)
            System.Diagnostics.Debug.WriteLine($"[HappyPhoton] {report}");
        var noun = reports.Count == 1 ? "issue" : "issues";
        ShowTransientStatus(
            $"XMP reconciliation reported {reports.Count} {noun}: {reports[0]}");
    }

    private async Task ObserveXmpReconcileAsync(
        Task task,
        CancellationTokenSource owner)
    {
        try { await task; }
        catch (OperationCanceledException) when (owner.IsCancellationRequested) { }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"XMP reconciliation failed: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_xmpReconcileCts, owner))
            {
                _xmpReconcileCts = null;
                _xmpReconcileTask = null;
            }
            owner.Dispose();
        }
    }

    private async Task CancelXmpReconcileAsync()
    {
        var cts = Interlocked.Exchange(ref _xmpReconcileCts, null);
        var task = Interlocked.Exchange(ref _xmpReconcileTask, null);
        if (cts == null) return;
        cts.Cancel();
        if (task != null)
        {
            try { await task; }
            catch (OperationCanceledException) { }
            catch { }
        }
    }

    private async Task<IReadOnlyList<AssessmentSnapshot>> CommitAssessmentAsync(
        IReadOnlyCollection<AssessmentMutation> mutations)
    {
        var primaryIds = Browse.AllImages
            .Where(image => image.Version == 1)
            .Select(image => image.CatalogId)
            .ToHashSet();
        var catalogMutations = mutations.Select(mutation => mutation with
        {
            PendingAxes = XmpSidecarMode == XmpSidecarMode.ReadWrite &&
                primaryIds.Contains(mutation.ImageId)
                ? mutation.Axes
                : AssessmentAxes.None
        }).ToArray();
        var snapshots = await _catalogService.MutateAssessmentsAsync(
            catalogMutations);
        foreach (var snapshot in snapshots)
        {
            var image = Browse.AllImages.FirstOrDefault(candidate =>
                candidate.CatalogId == snapshot.ImageId);
            if (image != null) ApplyAssessmentSnapshot(image, snapshot);
        }
        if (catalogMutations.Any(mutation =>
                mutation.PendingAxes != AssessmentAxes.None) &&
            _xmpWriter != null)
        {
            var paths = Browse.AllImages.Where(image => image.Version == 1)
                .Select(image => image.FilePath).ToArray();
            foreach (var snapshot in snapshots)
            {
                if (!primaryIds.Contains(snapshot.ImageId) ||
                    IsDeleteTargetClaimed(snapshot.FilePath)) continue;
                _xmpWriter.TryEnqueue(
                    snapshot, snapshot.PendingAxes, paths, XmpSidecarNaming);
            }
        }
        return snapshots;
    }

    private CropWriteContext CaptureCropWriteContext(ImageFile image)
    {
        var folderImagePaths = Browse.AllImages
            .Where(candidate => candidate.Version == 1)
            .Select(candidate => candidate.FilePath)
            .Append(image.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new CropWriteContext(
            image.Version == 1 && XmpSidecarMode == XmpSidecarMode.ReadWrite,
            _xmpWriter,
            folderImagePaths,
            XmpSidecarNaming);
    }

    private async Task CommitCropAssessmentAsync(
        ImageFile image,
        CropWriteContext writeContext)
    {
        var snapshot = (await _catalogService.MutateAssessmentsAsync([
            new AssessmentMutation(
                image.CatalogId,
                AssessmentAxes.Crop,
                PendingAxes: writeContext.WritePending
                    ? AssessmentAxes.Crop
                    : AssessmentAxes.None)
        ])).Single();
        ApplyAssessmentSnapshot(image, snapshot);
        if (!writeContext.WritePending || writeContext.Writer == null ||
            IsDeleteTargetClaimed(snapshot.FilePath))
        {
            return;
        }
        writeContext.Writer.TryEnqueue(
            snapshot,
            AssessmentAxes.Crop,
            writeContext.FolderImagePaths,
            writeContext.Naming);
    }

    private void SetDeleteTargetsClaimed(
        IEnumerable<string> paths,
        bool claimed)
    {
        lock (_inFlightDeletePaths)
        {
            foreach (var path in paths)
            {
                if (claimed) _inFlightDeletePaths.Add(path);
                else _inFlightDeletePaths.Remove(path);
            }
        }
    }

    private bool IsDeleteTargetClaimed(string path)
    {
        lock (_inFlightDeletePaths) return _inFlightDeletePaths.Contains(path);
    }

    private static void ApplyAssessmentSnapshot(
        ImageFile image,
        AssessmentSnapshot snapshot,
        AssessmentAxes appliedAxes = AssessmentAxes.All)
    {
        if (appliedAxes.HasFlag(AssessmentAxes.Flag))
            image.Flag = snapshot.Flag;
        if (appliedAxes.HasFlag(AssessmentAxes.Rating))
            image.Rating = snapshot.Rating;
        if (appliedAxes.HasFlag(AssessmentAxes.Label))
            image.ColorLabel = snapshot.ColorLabel;
        image.AssessmentRevision = snapshot.Revision;
        image.AssessedUtc = snapshot.AssessedUtc;
        image.PendingAssessmentAxes = snapshot.PendingAxes;
    }

    private void ReportPendingXmpAssessments(IEnumerable<ImageFile> images)
    {
        var count = images.Count(image =>
            image.Version == 1 &&
            image.PendingAssessmentAxes != AssessmentAxes.None);
        if (count == 0) return;
        var noun = count == 1 ? "photo" : "photos";
        ShowTransientStatus($"XMP writes remain pending for {count} {noun}");
    }

    private readonly record struct CropWriteContext(
        bool WritePending,
        XmpSidecarWriter? Writer,
        IReadOnlyCollection<string> FolderImagePaths,
        XmpSidecarNaming Naming);
}

using Avalonia.Threading;
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
        foreach (var adoption in result.Adoptions)
        {
            if (!byPath.TryGetValue(adoption.Snapshot.FilePath, out var image) ||
                image.AssessmentRevision + 1 != adoption.Snapshot.Revision)
            {
                continue;
            }
            if (image.CatalogId == 0)
                image.CatalogId = adoption.Snapshot.ImageId;
            if (adoption.AdoptedAxes.HasFlag(AssessmentAxes.Rating))
                image.Rating = adoption.Snapshot.Rating;
            if (adoption.AdoptedAxes.HasFlag(AssessmentAxes.Flag))
                image.Flag = adoption.Snapshot.Flag;
            if (adoption.AdoptedAxes.HasFlag(AssessmentAxes.Label))
                image.ColorLabel = adoption.Snapshot.ColorLabel;
            ApplyAssessmentSnapshot(image, adoption.Snapshot);
        }
        if (result.Adoptions.Count > 0) Browse.RefreshFilters();
        ReportXmpReconcileIssues(result.Reports);
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
        AssessmentSnapshot snapshot)
    {
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
}

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
            await StartXmpReconcileAsync(Volatile.Read(ref _libraryGeneration));
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
            Library.AllImages.Count == 0 ||
            generation != Volatile.Read(ref _libraryGeneration))
        {
            return;
        }
        var cts = new CancellationTokenSource();
        _xmpReconcileCts = cts;
        var paths = Library.AllImages.Select(image => image.FilePath).ToArray();
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
            generation != Volatile.Read(ref _libraryGeneration) ||
            !ReferenceEquals(_xmpReconcileCts, owner))
        {
            return;
        }
        var byPath = Library.AllImages.ToDictionary(
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
        if (result.Adoptions.Count > 0) Library.RefreshFilters();
        if (result.Reports.Count > 0)
        {
            foreach (var report in result.Reports)
                System.Diagnostics.Debug.WriteLine($"[HappyPhoton] {report}");
        }
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
        var pending = XmpSidecarMode == XmpSidecarMode.ReadWrite
            ? mutations.Aggregate(AssessmentAxes.None,
                (axes, mutation) => axes | mutation.Axes)
            : AssessmentAxes.None;
        var snapshots = await _catalogService.MutateAssessmentsAsync(
            mutations, pending);
        foreach (var snapshot in snapshots)
        {
            var image = Library.AllImages.FirstOrDefault(candidate =>
                candidate.CatalogId == snapshot.ImageId);
            if (image != null) ApplyAssessmentSnapshot(image, snapshot);
        }
        if (pending != AssessmentAxes.None && _xmpWriter != null)
        {
            var paths = Library.AllImages.Select(image => image.FilePath).ToArray();
            foreach (var snapshot in snapshots)
            {
                _xmpWriter.TryEnqueue(
                    snapshot, snapshot.PendingAxes, paths, XmpSidecarNaming);
            }
        }
        return snapshots;
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
            image.PendingAssessmentAxes != AssessmentAxes.None);
        if (count == 0) return;
        var noun = count == 1 ? "photo" : "photos";
        ShowTransientStatus($"XMP writes remain pending for {count} {noun}");
    }
}

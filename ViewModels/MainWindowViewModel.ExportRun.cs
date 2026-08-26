using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public sealed record ExportRunReport(
    string Heading,
    string Summary,
    IReadOnlyList<ExportTargetOutcome> FailedTargets,
    IReadOnlyList<ExportWarning> Warnings)
{
    public bool IsVisible => !string.IsNullOrEmpty(Heading);
    public bool HasFailures => FailedTargets.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;

    public static ExportRunReport Message(string heading, string summary) =>
        new(heading, summary, [], []);

    public static ExportRunReport FromResult(ExportBatchResult result)
    {
        var total = result.Outcomes.Count;
        var successful = result.SuccessfulTargetCount;
        var heading = result.FailedTargets.Count > 0
            ? "Export finished with failures"
            : result.Warnings.Count > 0
                ? "Export finished with warnings"
                : "Export complete";
        return new ExportRunReport(
            heading,
            $"{successful} of {total} files exported.",
            result.FailedTargets,
            result.Warnings);
    }
}

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _exportJobCancellation;
    private Task? _exportJobTask;
    private ExportJob? _failedExportJob;
    private int _exportStartOwned;
    private int _exportDisposing;
    private int _duplicateExportStartRefusals;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExportQueueVisible))]
    [NotifyPropertyChangedFor(nameof(IsBackgroundActivityStatusVisible))]
    [NotifyPropertyChangedFor(nameof(CanRunExport))]
    private bool _isExportJobRunning;

    [ObservableProperty]
    private int _exportProgressValue;

    [ObservableProperty]
    private int _exportProgressMaximum = 1;

    [ObservableProperty]
    private string _exportProgressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExportReport))]
    private ExportRunReport? _exportReport;

    public Func<int, IReadOnlyList<string>, Task<bool>>?
        ConfirmExportOverwriteAsync { get; set; }

    public Func<ExportHydrationScope, Task<bool>>?
        ConfirmExportHydrationAsync { get; set; }

    public bool IsExportQueueVisible => IsExportMode && IsExportJobRunning;
    public bool HasExportReport => ExportReport?.IsVisible == true;
    public bool CanRunExport => IsExportMode && !IsExportJobRunning &&
        ExportFileCount > 0 &&
        !string.IsNullOrWhiteSpace(ExportSettings.OutputFolder);
    public bool CanRetryFailedExport => IsExportMode && !IsExportJobRunning &&
        _failedExportJob is { Targets.Count: > 0 };

    internal int DuplicateExportStartRefusalCount =>
        Volatile.Read(ref _duplicateExportStartRefusals);
    internal Task? ActiveExportJobTask => _exportJobTask;
    internal event Action? ExportJobDrainStarted;
    internal event Action? ExportJobDrainCompleted;
    internal event Action? DependentExportServicesDisposing;

    internal Task RunExportJobForTestAsync(ExportJob job) =>
        TryStartExportAsync(job);

    [RelayCommand(CanExecute = nameof(CanRunExport))]
    private Task RunExportAsync()
    {
        if (!IsExportMode || Volatile.Read(ref _exportDisposing) != 0)
            return Task.CompletedTask;
        return TryStartExportAsync(job: null);
    }

    [RelayCommand(CanExecute = nameof(CanRetryFailedExport))]
    private Task RetryFailedExportAsync()
    {
        if (_failedExportJob == null) return Task.CompletedTask;
        return TryStartExportAsync(_failedExportJob);
    }

    private Task TryStartExportAsync(ExportJob? job)
    {
        if (Interlocked.CompareExchange(ref _exportStartOwned, 1, 0) != 0)
        {
            Interlocked.Increment(ref _duplicateExportStartRefusals);
            return Task.CompletedTask;
        }

        var cancellation = new CancellationTokenSource();
        _exportJobCancellation = cancellation;
        var task = RunExportOwnedAsync(job, cancellation);
        _exportJobTask = task;
        return task;
    }

    private async Task RunExportOwnedAsync(
        ExportJob? retryJob,
        CancellationTokenSource cancellation)
    {
        try
        {
            ExportReport = null;
            _failedExportJob = null;
            NotifyExportRunCommandState();
            var job = retryJob ?? ExportSettings.CreateJob(ExportCaptures
                .Where(capture => capture.IsIncluded)
                .Select(capture => capture.Image));
            var preflight = await PreflightExportAsync(
                job,
                cancellation.Token);
            if (preflight == null) return;

            cancellation.Token.ThrowIfCancellationRequested();
            IsExportJobRunning = true;
            ExportProgressValue = 0;
            ExportProgressMaximum = Math.Max(1, preflight.Job.Targets.Count);
            ExportProgressText = "Preparing export…";
            NotifyExportRunCommandState();

            var result = await ExportJobAsync(
                preflight.Job,
                preflight.HydrationApproved,
                new Progress<(int current, int total, string fileName)>(
                    UpdateExportProgress),
                cancellation.Token);
            ExportReport = ExportRunReport.FromResult(result);
            _failedExportJob = ProjectFailedTargets(preflight.Job, result);
        }
        catch (OperationCanceledException)
        {
            if (Volatile.Read(ref _exportDisposing) == 0)
            {
                ExportReport = ExportRunReport.Message(
                    "Export stopped",
                    "No further files will be exported.");
            }
        }
        catch (Exception exception)
        {
            ExportReport = ExportRunReport.Message(
                "Export failed",
                exception.Message);
        }
        finally
        {
            IsExportJobRunning = false;
            cancellation.Dispose();
            Interlocked.CompareExchange(
                ref _exportJobCancellation,
                null,
                cancellation);
            Volatile.Write(ref _exportStartOwned, 0);
            NotifyExportRunCommandState();
        }
    }

    private async Task<ExportPreflight?> PreflightExportAsync(
        ExportJob job,
        CancellationToken cancellationToken)
    {
        if (job.Targets.Count == 0)
        {
            ExportReport = ExportRunReport.Message(
                "Nothing to export",
                "Include at least one capture and arm at least one recipe.");
            return null;
        }

        var originals = ExportSafety.BuildOriginalPathSet(
            Browse.AllImages.Select(image => image.FilePath));
        var originalCollisions = job.Targets.Count(target =>
            ExportSafety.IsOriginalPath(target.ResolvedPath, originals));
        if (originalCollisions > 0)
        {
            ExportReport = ExportRunReport.Message(
                "Export blocked",
                $"{originalCollisions} export target" +
                $"{(originalCollisions == 1 ? string.Empty : "s")} would overwrite " +
                "a loaded original. Choose another destination or naming pattern.");
            return null;
        }

        if (job.HasPathCollisions)
        {
            ExportReport = ExportRunReport.Message(
                "Export blocked",
                GetPathCollisionMessage(job));
            return null;
        }

        var existingPaths = job.Targets
            .Select(target => target.ResolvedPath)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (existingPaths.Count > 0 &&
            (ConfirmExportOverwriteAsync == null ||
             !await ConfirmExportOverwriteAsync(existingPaths.Count, existingPaths)))
        {
            return null;
        }
        cancellationToken.ThrowIfCancellationRequested();

        var hydration = GetExportHydrationScope(job.Captures);
        var hydrationApproved = false;
        if (hydration.IsRequired)
        {
            hydrationApproved = ConfirmExportHydrationAsync != null &&
                await ConfirmExportHydrationAsync(hydration);
            if (!hydrationApproved) return null;
        }
        cancellationToken.ThrowIfCancellationRequested();

        return new ExportPreflight(
            job.AuthorizeOverwrites(existingPaths),
            hydrationApproved);
    }

    private static string GetPathCollisionMessage(ExportJob job)
    {
        foreach (var collision in job.PathCollisions)
        {
            var captures = collision.Targets
                .Select(target => target.Capture)
                .Distinct()
                .ToList();
            if (collision.Targets.Count != 2 || captures.Count != 2 ||
                !CapturePairingService.IsRawJpegPair(
                    captures[0].FilePath,
                    captures[1].FilePath))
            {
                continue;
            }

            return $"{captures[0].FileName} and {captures[1].FileName} are one " +
                "capture shot RAW+JPEG. Both would export to " +
                $"{Path.GetFileName(collision.ResolvedPath)}. Uncheck one in the " +
                "Export filmstrip and run Export again.";
        }

        return $"{job.PathCollisions.Count} output path" +
            $"{(job.PathCollisions.Count == 1 ? string.Empty : "s")} is shared " +
            "by multiple targets. Adjust the armed recipes or uncheck one of the " +
            "colliding captures in the Export filmstrip.";
    }

    private void UpdateExportProgress(
        (int current, int total, string fileName) value)
    {
        ExportProgressMaximum = Math.Max(1, value.total);
        ExportProgressValue = Math.Clamp(value.current, 0, value.total);
        ExportProgressText = value.current >= value.total
            ? $"{value.total}/{value.total} files finished"
            : $"Exporting {value.current}/{value.total} — {value.fileName}";
    }

    private static ExportJob? ProjectFailedTargets(
        ExportJob job,
        ExportBatchResult result)
    {
        if (result.FailedTargets.Count == 0) return null;
        var failed = job.Targets.Where(target => result.FailedTargets.Any(
            outcome => ReferenceEquals(outcome.Capture, target.Capture) &&
                outcome.Recipe == target.Recipe &&
                string.Equals(
                    outcome.ResolvedPath,
                    target.ResolvedPath,
                    StringComparison.OrdinalIgnoreCase)));
        return job.ProjectTargets(failed);
    }

    private void NotifyExportRunCommandState()
    {
        RunExportCommand.NotifyCanExecuteChanged();
        RetryFailedExportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanRunExport));
        OnPropertyChanged(nameof(CanRetryFailedExport));
        OnPropertyChanged(nameof(IsExportQueueVisible));
        OnPropertyChanged(nameof(IsBackgroundActivityStatusVisible));
    }

    private async Task CancelAndDrainExportJobAsync()
    {
        Volatile.Write(ref _exportDisposing, 1);
        var cancellation = Volatile.Read(ref _exportJobCancellation);
        var task = Volatile.Read(ref _exportJobTask);
        if (task == null) return;

        ExportJobDrainStarted?.Invoke();
        cancellation?.Cancel();
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        ExportJobDrainCompleted?.Invoke();
    }

    private sealed record ExportPreflight(
        ExportJob Job,
        bool HydrationApproved);

    public Task<ExportBatchResult> ExportBatchAsync(
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExportBatchAsync(GetSelectedImages().ToList(), progress, cancellationToken);

    public Task<ExportBatchResult> ExportBatchAsync(
        IReadOnlyList<ImageFile> imagesToExport,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var job = ExportSettings.CreateJob(imagesToExport);
        return ExportJobAsync(job, false, progress, cancellationToken);
    }

    internal ExportHydrationScope GetExportHydrationScope(
        IReadOnlyList<ImageFile> images) =>
        ImageService.GetExportHydrationScope(images);

    internal Task<ExportBatchResult> ExportBatchApprovedAsync(
        IReadOnlyList<ImageFile> imagesToExport,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var job = ExportSettings.CreateJob(imagesToExport);
        return ExportJobAsync(job, true, progress, cancellationToken);
    }

    private async Task<ExportBatchResult> ExportJobAsync(
        ExportJob job,
        bool hydrationApproved,
        IProgress<(int current, int total, string fileName)>? progress,
        CancellationToken cancellationToken)
    {
        using var activity = BeginExportActivity(job.Targets.Count);
        var activityProgress = CreateExportActivityProgress(activity, progress);
        var generation = Volatile.Read(ref _browseGeneration);
        var result = hydrationApproved
            ? await ImageService.ExportBatchApprovedAsync(
                job,
                activityProgress,
                cancellationToken)
            : await ImageService.ExportBatchAsync(
                job,
                activityProgress,
                cancellationToken);
        if (hydrationApproved)
        {
            RefreshExportHydratedSources(
                result.Outcomes
                    .Where(outcome => outcome.Succeeded)
                    .Select(outcome => outcome.Capture)
                    .Distinct()
                    .ToList(),
                generation,
                cancellationToken);
        }
        return result;
    }

    private static IProgress<(int current, int total, string fileName)>
        CreateExportActivityProgress(
            BackgroundExportActivityRegistry.BackgroundExportScope activity,
            IProgress<(int current, int total, string fileName)>? progress) =>
        new ExportActivityProgress(activity, progress);

    private sealed class ExportActivityProgress(
        BackgroundExportActivityRegistry.BackgroundExportScope activity,
        IProgress<(int current, int total, string fileName)>? progress) :
        IProgress<(int current, int total, string fileName)>
    {
        public void Report((int current, int total, string fileName) value)
        {
            activity.Report(value.current);
            progress?.Report(value);
        }
    }

    private void RefreshExportHydratedSources(
        IReadOnlyList<ImageFile> images,
        int generation,
        CancellationToken cancellationToken)
    {
        var targets = new List<ImageFile>();
        foreach (var image in images)
        {
            if (cancellationToken.IsCancellationRequested ||
                generation != Volatile.Read(ref _browseGeneration))
            {
                return;
            }

            if (!Browse.Contains(image) ||
                (!image.SourceRequiresHydration &&
                 !image.ThumbnailDeferredForHydration &&
                 image.ThumbnailUpgradeDeferredDimension == 0) ||
                !ImageService.CanRetryBackgroundRead(image))
            {
                continue;
            }

            SetSourceRequiresHydration(image, false);
            image.ThumbnailDeferredForHydration = false;
            image.ThumbnailLoadFailed = false;
            image.ThumbnailUpgradeDeferredDimension = 0;
            image.ThumbnailUpgradeFailedDimension = 0;
            targets.Add(image);
        }

        if (targets.Count == 0) return;
        var scheduler = _thumbnailScheduler;
        if (scheduler != null)
        {
            scheduler.Enqueue(targets.Select(image =>
                new ThumbnailLoadRequest(
                    image,
                    BrowseThumbnailRequest,
                    0)));
            SignalBackgroundActivityStarted();
            return;
        }

        _ = TrackDirectThumbnailOperation(
            RefreshExportHydratedSourcesDirectAsync(
                targets,
                generation,
                cancellationToken));
    }

    private async Task RefreshExportHydratedSourcesDirectAsync(
        IReadOnlyList<ImageFile> images,
        int generation,
        CancellationToken cancellationToken)
    {
        foreach (var image in images)
        {
            if (cancellationToken.IsCancellationRequested ||
                generation != Volatile.Read(ref _browseGeneration))
            {
                return;
            }
            await LoadThumbnailAsync(image, generation, cancellationToken);
        }
    }
}

using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private string? _lastAutomaticExportFolder;

    [RelayCommand]
    private void ToggleSelection()
    {
        if (IsFullScreenMode) return;

        if (SelectedImage != null)
        {
            Library.ToggleSelection(SelectedImage);
            UpdateSelectedCount();
        }
    }

    public void ToggleImageSelection(ImageFile image)
    {
        Library.ToggleSelection(image);
        UpdateSelectedCount();
    }

    public void SelectRange(ImageFile fromImage, ImageFile toImage)
    {
        Library.SelectRange(fromImage, toImage);
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void SelectAll()
    {
        if (IsFullScreenMode) return;

        Library.SelectAllVisible();
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        if (IsFullScreenMode) return;

        Library.DeselectAllVisible();
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = Library.SelectedCount;
        RestartLibrarySelectionSummary();
        ReconcileFullScreenSelection();
    }

    public void RefreshSelectedCount()
    {
        UpdateSelectedCount();
    }

    [RelayCommand]
    private Task ShowExportDialogAsync() =>
        ShowExportDialogAsync(ExportDialogMode.Standard);

    private async Task ShowExportDialogAsync(ExportDialogMode mode)
    {
        if (IsFullScreenMode) return;
        UpdateAutomaticExportFolder();

        if (RequestExportDialogAsync != null)
        {
            await RequestExportDialogAsync(mode);
        }
    }

    public IEnumerable<ImageFile> GetSelectedImages()
    {
        return Library.GetSelectedImages();
    }

    public Task<ExportBatchResult> ExportBatchAsync(
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExportBatchAsync(GetSelectedImages().ToList(), progress, cancellationToken);

    public async Task<ExportBatchResult> ExportBatchAsync(
        IReadOnlyList<ImageFile> imagesToExport,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = BeginExportActivity(imagesToExport.Count);
        var activityProgress = CreateExportActivityProgress(activity, progress);
        return await ImageService.ExportBatchAsync(
            imagesToExport,
            ExportSettings,
            activityProgress,
            cancellationToken);
    }

    internal ExportHydrationScope GetExportHydrationScope(
        IReadOnlyList<ImageFile> images) =>
        ImageService.GetExportHydrationScope(images);

    internal async Task<ExportBatchResult> ExportBatchApprovedAsync(
        IReadOnlyList<ImageFile> imagesToExport,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var activity = BeginExportActivity(imagesToExport.Count);
        var activityProgress = CreateExportActivityProgress(activity, progress);
        var generation = Volatile.Read(ref _libraryGeneration);
        var exported = await ImageService.ExportBatchApprovedAsync(
            imagesToExport,
            ExportSettings,
            activityProgress,
            cancellationToken);

        try
        {
            RefreshExportHydratedSources(
                imagesToExport,
                generation,
                cancellationToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Post-export Library refresh failed: {ex.Message}");
        }

        return exported;
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
                generation != Volatile.Read(ref _libraryGeneration))
            {
                return;
            }

            if (!Library.Contains(image) ||
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
                    LibraryThumbnailRequest,
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
                generation != Volatile.Read(ref _libraryGeneration))
            {
                return;
            }
            await LoadThumbnailAsync(image, generation, cancellationToken);
        }
    }

    private static IProgress<(int current, int total, string fileName)>
        CreateExportActivityProgress(
            BackgroundExportActivityRegistry.BackgroundExportScope activity,
            IProgress<(int current, int total, string fileName)>? progress) =>
        new ExportActivityProgress(activity, progress);

    private sealed class ExportActivityProgress :
        IProgress<(int current, int total, string fileName)>
    {
        private readonly BackgroundExportActivityRegistry.BackgroundExportScope
            _activity;
        private readonly IProgress<(int current, int total, string fileName)>?
            _progress;

        public ExportActivityProgress(
            BackgroundExportActivityRegistry.BackgroundExportScope activity,
            IProgress<(int current, int total, string fileName)>? progress)
        {
            _activity = activity;
            _progress = progress;
        }

        public void Report((int current, int total, string fileName) value)
        {
            _activity.Report(value.current);
            _progress?.Report(value);
        }
    }

    /// <summary>
    /// Handles Escape key: cancels crop or exits develop mode.
    /// Does nothing in Library view when no transient workspace mode is active.
    /// </summary>
    [RelayCommand]
    private void HandleEscape()
    {
        if (IsWhiteBalancePicking)
        {
            IsWhiteBalancePicking = false;
            ShowTransientStatus("White balance picker canceled");
            return;
        }

        if (IsFullScreenMode)
        {
            IsFullScreenMode = false;
            return;
        }

        // First priority: cancel crop mode if active
        if (IsCropMode)
        {
            CancelCrop();
            return;
        }

        // Second priority: exit Develop mode to Library (but do nothing if already in Library)
        if (IsDevelopMode)
        {
            IsDevelopMode = false;
        }
    }

    private void UpdateAutomaticExportFolder()
    {
        if (string.IsNullOrEmpty(CurrentFolderPath)) return;

        var nextDefault = Path.Combine(CurrentFolderPath, "export");
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.IsNullOrEmpty(ExportSettings.OutputFolder) ||
            (_lastAutomaticExportFolder != null &&
             string.Equals(
                 ExportSettings.OutputFolder,
                 _lastAutomaticExportFolder,
                 comparison)))
        {
            ExportSettings.OutputFolder = nextDefault;
        }

        _lastAutomaticExportFolder = nextDefault;
    }
}

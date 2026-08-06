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

    public Task<int> ExportBatchAsync(
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExportBatchAsync(GetSelectedImages().ToList(), progress, cancellationToken);

    public Task<int> ExportBatchAsync(
        IReadOnlyList<ImageFile> imagesToExport,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default) =>
        ImageService.ExportBatchAsync(imagesToExport, ExportSettings, progress, cancellationToken);

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

using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{    [RelayCommand]
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
    private void ShowExportPanel()
    {
        if (IsFullScreenMode) return;

        if (SelectedCount == 0 && SelectedImage != null)
        {
            // If nothing selected, select current image
            Library.ToggleSelection(SelectedImage);
            UpdateSelectedCount();
        }

        if (SelectedCount > 0)
        {
            // Set default output folder
            if (string.IsNullOrEmpty(ExportSettings.OutputFolder) && !string.IsNullOrEmpty(CurrentFolderPath))
            {
                ExportSettings.OutputFolder = Path.Combine(CurrentFolderPath, "exports");
            }

            IsExportPanelVisible = true;
        }
    }

    [RelayCommand]
    private void HideExportPanel()
    {
        IsExportPanelVisible = false;
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
    /// Handles Escape key: closes export panel, cancels crop, or exits develop mode.
    /// Does nothing in Library view when no panels are open.
    /// </summary>
    [RelayCommand]
    private void HandleEscape()
    {
        if (IsFullScreenMode)
        {
            IsFullScreenMode = false;
            return;
        }

        // First priority: close export panel if open
        if (IsExportPanelVisible)
        {
            IsExportPanelVisible = false;
            return;
        }

        // Second priority: cancel crop mode if active
        if (IsCropMode)
        {
            CancelCrop();
            return;
        }

        // Third priority: exit Develop mode to Library (but do nothing if already in Library)
        if (IsDevelopMode)
        {
            IsDevelopMode = false;
        }
    }
}

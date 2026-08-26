using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private string? _lastAutomaticExportFolder;
    private ExportCaptureViewModel? _activeExportCapture;
    private bool _exportSettingsObserved;

    public ObservableCollection<ExportCaptureViewModel> ExportCaptures { get; } = [];

    public ExportCaptureViewModel? ActiveExportCapture
    {
        get => _activeExportCapture;
        set
        {
            if (!SetProperty(ref _activeExportCapture, value) || value == null) return;
            SelectedImage = value.Image;
        }
    }

    public bool HasNoExportCaptures => ExportCaptures.Count == 0;
    public int IncludedExportCaptureCount =>
        ExportCaptures.Count(capture => capture.IsIncluded);
    public int ArmedExportRecipeCount =>
        (ExportSettings.ExportHiRes ? 1 : 0) +
        (ExportSettings.ExportWeb ? 1 : 0) +
        (ExportSettings.ExportSmall ? 1 : 0);
    public int ExportFileCount =>
        IncludedExportCaptureCount * ArmedExportRecipeCount;
    public string ExportCountLine =>
        $"{IncludedExportCaptureCount} " +
        $"{(IncludedExportCaptureCount == 1 ? "capture" : "captures")} × " +
        $"{ArmedExportRecipeCount} " +
        $"{(ArmedExportRecipeCount == 1 ? "recipe" : "recipes")} → " +
        $"{ExportFileCount} {(ExportFileCount == 1 ? "file" : "files")}";
    public bool IsExportQualityAvailable =>
        ExportSettings.Format is not ExportFormat.Png and not ExportFormat.Tiff;

    [RelayCommand]
    private void ToggleSelection()
    {
        if (IsFullScreenMode || IsExportMode) return;

        if (SelectedImage != null)
        {
            Browse.ToggleSelection(SelectedImage);
            UpdateSelectedCount();
        }
    }

    public void ToggleImageSelection(ImageFile image)
    {
        Browse.ToggleSelection(image);
        UpdateSelectedCount();
    }

    public void SelectRange(ImageFile fromImage, ImageFile toImage)
    {
        Browse.SelectRange(fromImage, toImage);
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void SelectAll()
    {
        if (IsFullScreenMode || IsExportMode) return;

        Browse.SelectAllVisible();
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        if (IsFullScreenMode || IsExportMode) return;

        Browse.DeselectAllVisible();
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        SelectedCount = Browse.SelectedCount;
        RestartBrowseSelectionSummary();
        ReconcileFullScreenSelection();
    }

    public void RefreshSelectedCount()
    {
        UpdateSelectedCount();
    }

    public IEnumerable<ImageFile> GetSelectedImages()
    {
        return Browse.GetSelectedImages();
    }

    /// <summary>
    /// Handles Escape key: cancels crop or exits develop mode.
    /// Does nothing in Browse view when no transient workspace mode is active.
    /// </summary>
    [RelayCommand]
    private void HandleEscape()
    {
        // A held loupe outranks everything: Escape releases the peek without
        // also leaving the view it was peeking in.
        if (CancelActiveLoupePeek?.Invoke() == true)
        {
            return;
        }

        if (IsCompareMode)
        {
            CloseCompare();
            return;
        }

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

        if (IsExportMode)
        {
            WorkspaceMode = _workspaceModeBeforeExport;
            return;
        }

        // Second priority: exit Develop mode to Browse (but do nothing if already in Browse)
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

    private void PrepareExportWorkspace()
    {
        UpdateAutomaticExportFolder();
        foreach (var capture in ExportCaptures)
        {
            capture.PropertyChanged -= OnExportCapturePropertyChanged;
        }
        ExportCaptures.Clear();
        foreach (var image in Browse.GetSelectedImages())
        {
            var capture = new ExportCaptureViewModel(image);
            capture.PropertyChanged += OnExportCapturePropertyChanged;
            ExportCaptures.Add(capture);
        }

        ActiveExportCapture = ExportCaptures.FirstOrDefault(capture =>
            ReferenceEquals(capture.Image, SelectedImage)) ?? ExportCaptures.FirstOrDefault();
        if (!_exportSettingsObserved)
        {
            ExportSettings.PropertyChanged += OnWorkspaceExportSettingsChanged;
            _exportSettingsObserved = true;
        }
        NotifyExportWorkspaceCounts();
        OnPropertyChanged(nameof(HasNoExportCaptures));
    }

    private bool TryMoveWithinExportSelection(int offset)
    {
        if (!IsExportMode) return false;
        if (ExportCaptures.Count == 0) return true;
        var current = ActiveExportCapture == null
            ? -1
            : ExportCaptures.IndexOf(ActiveExportCapture);
        var next = Math.Clamp(current + offset, 0, ExportCaptures.Count - 1);
        if (next >= 0) ActiveExportCapture = ExportCaptures[next];
        return true;
    }

    private void OnExportCapturePropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ExportCaptureViewModel.IsIncluded))
            NotifyExportWorkspaceCounts();
    }

    private void OnWorkspaceExportSettingsChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ExportSettings.ExportHiRes) or
            nameof(ExportSettings.ExportWeb) or nameof(ExportSettings.ExportSmall))
            NotifyExportWorkspaceCounts();
        if (args.PropertyName == nameof(ExportSettings.Format))
            OnPropertyChanged(nameof(IsExportQualityAvailable));
        if (args.PropertyName == nameof(ExportSettings.OutputFolder))
            NotifyExportRunCommandState();
    }

    private void NotifyExportWorkspaceCounts()
    {
        OnPropertyChanged(nameof(IncludedExportCaptureCount));
        OnPropertyChanged(nameof(ArmedExportRecipeCount));
        OnPropertyChanged(nameof(ExportFileCount));
        OnPropertyChanged(nameof(ExportCountLine));
        NotifyExportRunCommandState();
    }
}

public sealed partial class ExportCaptureViewModel(ImageFile image) : ObservableObject
{
    public ImageFile Image { get; } = image;

    [ObservableProperty]
    private bool _isIncluded = true;
}

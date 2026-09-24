using System.Collections.ObjectModel;
using System.ComponentModel;
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
            OnPropertyChanged(nameof(ExportPathExample));
        }
    }

    public bool HasNoExportCaptures => ExportCaptures.Count == 0;
    public int ArmedExportSizeCount =>
        (ExportSettings.ExportHiRes ? 1 : 0) +
        (ExportSettings.ExportWeb ? 1 : 0) +
        (ExportSettings.ExportSmall ? 1 : 0);
    public int ExportFileCount => ExportCaptures.Count * ArmedExportSizeCount;
    public string ExportPhotoCount => $"{ExportCaptures.Count} {(ExportCaptures.Count == 1 ? "photo" : "photos")}";
    public string ExportCountLine => $"{ExportPhotoCount} · {ArmedExportSizeCount} {(ArmedExportSizeCount == 1 ? "size" : "sizes")}";
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
    private async Task HandleEscapeAsync()
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

        if (IsLoupeMode)
        {
            CloseLoupe();
            return;
        }

        if (IsBeforeAfterSplit)
        {
            CloseBeforeAfterSplit();
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

        if (EscapeLocals()) return;

        // First priority: cancel crop mode if active
        if (IsCropMode)
        {
            await CancelCropAsync();
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
        ExportCaptures.Clear();
        foreach (var image in Browse.GetSelectedImages())
            ExportCaptures.Add(new ExportCaptureViewModel(image));
        _exportVersionedPaths = ExportJob.FindVersionedPaths(
            ExportCaptures.Select(capture => capture.Image));

        var activeImage = VisibleRepresentative(SelectedImage);
        ActiveExportCapture = ExportCaptures.FirstOrDefault(capture =>
            ReferenceEquals(capture.Image, activeImage)) ?? ExportCaptures.FirstOrDefault();
        if (!_exportSettingsObserved)
        {
            InitializeExportWatermark();
            ExportSettings.PropertyChanged += OnWorkspaceExportSettingsChanged;
            Browse.StateChanged += (_, _) => NotifyExportBatchSettings();
            _exportSettingsObserved = true;
        }
        RefreshExportProofSizes();
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

    private void OnWorkspaceExportSettingsChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ExportSettings.ExportHiRes) or
            nameof(ExportSettings.ExportWeb) or nameof(ExportSettings.ExportSmall))
            NotifyExportWorkspaceCounts();
        NotifyExportBatchSettings();
        var property = args.PropertyName;
        if (property == nameof(ExportSettings.Format))
            OnPropertyChanged(nameof(IsExportQualityAvailable));
        if (property == nameof(ExportSettings.OutputFolder))
            NotifyExportRunCommandState();
        if (ChangesProofSizes(property)) RefreshExportProofSizes();
        if (property == nameof(ExportSettings.ShowProof))
        {
            if (ExportSettings.ShowProof) RequestExportProofRefresh();
            else RestoreExportPreview();
        }
        else if (ExportSettings.ShowProof && property == nameof(ExportSettings.Watermark))
            RequestWatermarkProofRefresh();
        else if (ExportSettings.ShowProof && ChangesProofPixels(property))
            RequestExportProofRefresh();
    }

    private void NotifyExportWorkspaceCounts()
    {
        OnPropertyChanged(nameof(ExportPhotoCount));
        NotifyExportBatchSettings();
        OnPropertyChanged(nameof(ArmedExportSizeCount));
        OnPropertyChanged(nameof(ExportFileCount));
        OnPropertyChanged(nameof(ExportCountLine));
        OnPropertyChanged(nameof(IsExportProofCaptionVisible));
        OnPropertyChanged(nameof(ExportProofCaption));
        NotifyExportRunCommandState();
    }
}

public sealed record ExportCaptureViewModel(ImageFile Image);

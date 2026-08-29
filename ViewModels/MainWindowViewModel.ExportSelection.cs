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
    private bool _proofIsDisplayed;
    private Task? _proofTask;

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
    public bool IsExportProofCaptionVisible =>
        IsExportMode && !HasNoExportCaptures && PreviewImage != null;
    public string ExportProofCaption => FormatExportProofCaption(
        _proofIsDisplayed,
        ExportSettings.Format,
        ExportSettings.OutputColorSpace,
        ArmedExportRecipeCount > 0
            ? ResolveExportProofMaxDimension()
            : null);

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

        var activeImage = VisibleRepresentative(SelectedImage);
        ActiveExportCapture = ExportCaptures.FirstOrDefault(capture =>
            ReferenceEquals(capture.Image, activeImage)) ?? ExportCaptures.FirstOrDefault();
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
        var property = args.PropertyName;
        if (property == nameof(ExportSettings.Format))
            OnPropertyChanged(nameof(IsExportQualityAvailable));
        if (property == nameof(ExportSettings.OutputFolder))
            NotifyExportRunCommandState();
        if (ChangesProofFacts(property))
            OnPropertyChanged(nameof(ExportProofCaption));
        if (property == nameof(ExportSettings.ShowProof))
        {
            if (ExportSettings.ShowProof) RequestExportProofRefresh();
            else RestoreExportPreview();
        }
        else if (ExportSettings.ShowProof && ChangesProofPixels(property))
            RequestExportProofRefresh();
    }

    private static bool ChangesProofFacts(string? property) => property is
        nameof(ExportSettings.Format) or nameof(ExportSettings.OutputColorSpace) or
        nameof(ExportSettings.ExportHiRes) or nameof(ExportSettings.ExportWeb) or
        nameof(ExportSettings.ExportSmall) or nameof(ExportSettings.WebMaxSize) or
        nameof(ExportSettings.SmallMaxSize);

    private static bool ChangesProofPixels(string? property) =>
        ChangesProofFacts(property) ||
        property == nameof(ExportSettings.OutputSharpening);

    private int? ResolveExportProofMaxDimension() =>
        ExportSettings.GetActiveVariants() is { Count: > 0 } variants
            ? variants[0].MaxDimension
            : BaseImage.InteractivePreviewMaxDimension;

    private void RequestExportProofRefresh()
    {
        var image = SelectedImage;
        if (!IsExportMode || !ExportSettings.ShowProof || image == null ||
            _renderOutcomeChannelClosed) return;

        var generation = ReserveRenderOutcome(
            PreviewSurfaceIntent.Edited,
            promotionEligible: false);
        _previewLoadingCts?.Cancel();
        var requestCts = new CancellationTokenSource();
        _previewLoadingCts = requestCts;
        _ = RefreshExportProofAsync(image, generation, requestCts);
    }

    private void RestoreExportPreview()
    {
        SetProofDisplayed(false);
        var image = SelectedImage;
        if (!IsExportMode || image == null || _renderOutcomeChannelClosed) return;

        var generation = ReserveRenderOutcome();
        ApplySurfaceClearOutcome(image, generation);
        _ = LoadPreviewAsync(image, generation);
    }

    private async Task RefreshExportProofAsync(
        ImageFile image,
        long generation,
        CancellationTokenSource requestCts)
    {
        using var previewActivity = BeginInitialPreviewActivity();
        try
        {
            await LoadExportProofAsync(
                image,
                CaptureRestingSettings(),
                generation,
                requestCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_previewLoadingCts, requestCts))
            {
                _previewLoadingCts = null;
            }
            requestCts.Dispose();
        }
    }

    private Task<bool> LoadExportProofAsync(
        ImageFile image,
        EditSettings settings,
        long generation,
        CancellationToken cancellationToken)
    {
        var task = RenderExportProofAsync(
            image, settings, generation, cancellationToken);
        var pending = _proofTask;
        _proofTask = pending is { IsCompleted: false }
            ? Task.WhenAll(pending, task)
            : task;
        return task;
    }

    private async Task<bool> RenderExportProofAsync(
        ImageFile image,
        EditSettings settings,
        long generation,
        CancellationToken cancellationToken)
    {
        var bitmap = await ImageService.Previews.RenderProofAsync(
            image,
            settings,
            ResolveExportProofMaxDimension(),
            ExportSettings.OutputColorSpace,
            ExportSettings.OutputSharpening,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (bitmap == null ||
            !IsExportMode ||
            !ExportSettings.ShowProof ||
            generation != LatestPreviewOutcomeGeneration ||
            !ReferenceEquals(SelectedImage, image))
        {
            bitmap?.Dispose();
            return false;
        }

        CancelRestingPreview(clearParent: true);
        ReplacePreviewImage(bitmap, PreviewPaintSource.FreshRender, isProof: true);
        _lastAppliedEditSettings = settings.Clone();
        return true;
    }

    private void SetProofDisplayed(bool value)
    {
        if (_proofIsDisplayed == value) return;
        _proofIsDisplayed = value;
        OnPropertyChanged(nameof(ExportProofCaption));
    }

    internal static string FormatExportProofCaption(
        bool proofIsDisplayed,
        ExportFormat format,
        OutputColorSpace outputColorSpace,
        int? longEdge)
    {
        if (longEdge is <= 0) throw new ArgumentOutOfRangeException(nameof(longEdge));
        var label = proofIsDisplayed ? "PROOF" : "PREVIEW";
        var caption = $"{label} · {format.ToString().ToUpperInvariant()} · " +
            (outputColorSpace == OutputColorSpace.Srgb ? "sRGB" : "Display P3");
        return longEdge is { } pixels
            ? $"{caption} · {pixels} PX"
            : caption;
    }

    private void NotifyExportWorkspaceCounts()
    {
        OnPropertyChanged(nameof(IncludedExportCaptureCount));
        OnPropertyChanged(nameof(ArmedExportRecipeCount));
        OnPropertyChanged(nameof(ExportFileCount));
        OnPropertyChanged(nameof(ExportCountLine));
        OnPropertyChanged(nameof(IsExportProofCaptionVisible));
        OnPropertyChanged(nameof(ExportProofCaption));
        NotifyExportRunCommandState();
    }
}

public sealed partial class ExportCaptureViewModel(ImageFile image) : ObservableObject
{
    public ImageFile Image { get; } = image;

    [ObservableProperty]
    private bool _isIncluded = true;
}

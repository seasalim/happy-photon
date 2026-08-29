using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDevelopMode))]
    [NotifyPropertyChangedFor(nameof(IsBrowseMode))]
    [NotifyPropertyChangedFor(nameof(IsExportMode))]
    [NotifyPropertyChangedFor(nameof(IsBrowseOrDevelopMode))]
    // IsBrowseGridVisible also reads IsCompareMode, whose own notification fires
    // while leaving Browse — only this one covers the return trip.
    [NotifyPropertyChangedFor(nameof(IsBrowseGridVisible))]
    [NotifyPropertyChangedFor(nameof(IsBrowseChromeVisible))]
    [NotifyPropertyChangedFor(nameof(IsExportProofCaptionVisible))]
    private WorkspaceMode _workspaceMode;
    private WorkspaceMode _workspaceModeBeforeExport = WorkspaceMode.Browse;

    public bool IsDevelopMode
    {
        get => WorkspaceMode == WorkspaceMode.Develop;
        set => WorkspaceMode = value
            ? WorkspaceMode.Develop
            : WorkspaceMode.Browse;
    }

    public bool IsBrowseMode => WorkspaceMode == WorkspaceMode.Browse;

    public bool IsExportMode =>
        WorkspaceMode == WorkspaceMode.Export;

    public bool IsBrowseOrDevelopMode =>
        WorkspaceMode is WorkspaceMode.Browse or WorkspaceMode.Develop;

    public bool IsDevelopPreviewSurfaceActive =>
        IsDevelopMode && !IsFullScreenMode;

    public bool IsWorkspacePreviewSurfaceActive =>
        (IsDevelopMode || IsExportMode) && !IsFullScreenMode;

    public bool IsFullScreenPreviewSurfaceActive => IsFullScreenMode;

    [RelayCommand]
    private void SwitchToBrowse()
    {
        if (IsFullScreenMode)
        {
            return;
        }

        CloseLoupe();
        CloseCompare();
        IsDevelopMode = false;
    }

    [RelayCommand]
    private void SwitchToDevelop()
    {
        if (IsFullScreenMode)
        {
            return;
        }

        CloseLoupe();
        IsDevelopMode = true;
    }

    [RelayCommand]
    private void SwitchToExport()
    {
        if (IsFullScreenMode)
        {
            return;
        }

        if (IsCropMode)
        {
            CancelCrop();
        }
        _workspaceModeBeforeExport = WorkspaceMode;
        WorkspaceMode = WorkspaceMode.Export;
    }

    [RelayCommand]
    private void EnterDevelopMode()
    {
        if (!IsFullScreenMode && HasSelectedImage)
        {
            IsDevelopMode = true;
        }
    }

    [RelayCommand]
    private async Task HandleEnterAsync()
    {
        if (IsFullScreenMode) return;
        if (IsExportMode)
        {
            await RunExportAsync();
            return;
        }
        if (IsCropMode)
        {
            await ApplyCropAsync();
            return;
        }
        if (IsLoupeMode)
        {
            CloseLoupe();
            IsDevelopMode = true;
        }
        else if (IsCompareMode)
        {
            IsDevelopMode = true;
        }
        else if (IsBrowseGridVisible)
        {
            EnterLoupe();
        }
    }

    partial void OnWorkspaceModeChanged(WorkspaceMode value)
    {
        if (value != WorkspaceMode.Develop) CloseBeforeAfterSplit();
        if (value != WorkspaceMode.Browse)
        {
            CloseLoupe();
            CloseCompare();
        }
        // The compare gate reads the workspace too, so it needs the same re-notify.
        NotifyCompareGateChanged();
        if (value != WorkspaceMode.Export) SetProofDisplayed(false);
        var isDevelopMode = value == WorkspaceMode.Develop;
        var isPreviewWorkspace = value is WorkspaceMode.Develop or
            WorkspaceMode.Export;
        if (!isDevelopMode)
            SelectedImage = VisibleRepresentative(SelectedImage);
        UpdateThumbnailPumpAdmission();
        if (!isDevelopMode)
        {
            CancelAdjacentPreviewWarm(true, dropRetained: true);
            ClearAlignmentGrid();
        }
        // Cancel in-flight resting work but keep the parent: mode round-trips
        // on the same image must stay armed (publication is surface-gated and
        // render-time guards catch real staleness). Only selection changes
        // clear the parent.
        CancelRestingPreview(clearParent: false);
        OnPropertyChanged(nameof(IsDevelopPreviewSurfaceActive));
        OnPropertyChanged(nameof(IsWorkspacePreviewSurfaceActive));
        UpdateNavigatorPreviewSurfaceActivity();
        OnPropertyChanged(nameof(CanSavePreset));
        NotifyWorkflowTourVisibilityChanged();
        NotifyWhiteBalanceCommandState();
        ToggleColorAssessmentModeCommand.NotifyCanExecuteChanged();
        ToggleBeforeAfterCommand.NotifyCanExecuteChanged();
        ToggleBeforeAfterSplitCommand.NotifyCanExecuteChanged();
        NotifyCaptureMemberStateChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        NotifyClippingCommandState();
        NotifyExportRunCommandState();
        NotifyVersionCommandState();
        if (value == WorkspaceMode.Export)
        {
            PrepareExportWorkspace();
        }
        if (isPreviewWorkspace && SelectedImage != null)
        {
            var generation = ReserveRenderOutcome();
            ApplySurfaceClearOutcome(SelectedImage, generation);
            if (!isDevelopMode && !IsFullScreenMode)
            {
                LeaveDevelopClippingSurface();
            }
            _ = LoadPreviewAsync(SelectedImage, generation);
        }
        else if (!isPreviewWorkspace && !IsFullScreenMode)
        {
            var generation = ReserveRenderOutcome();
            ApplySurfaceClearOutcome(SelectedImage, generation);
            LeaveDevelopClippingSurface();
            _previewLoadingCts?.Cancel();
            _previewDebounce?.Cancel();
            if (SelectedImage is { IsRaw: true } image &&
                image.EditSettings.HasEdits)
            {
                _ = TrackDirectThumbnailOperation(
                    RefreshThumbnailAsync(image));
            }
            ImageService.Previews.FlushRenderedPreviewCache();
            ScheduleHistogramUpdate();
        }
        else if (!isDevelopMode && !IsFullScreenMode)
        {
            LeaveDevelopClippingSurface();
        }
    }

    [RelayCommand]
    private void ToggleFullScreen()
    {
        if (IsFullScreenMode)
        {
            IsFullScreenMode = false;
            return;
        }

        if (IsExportMode || IsCompareMode || !HasSelectedImage || IsCropMode)
        {
            return;
        }

        CloseLoupe();
        IsFullScreenMode = true;
    }

    partial void OnIsFullScreenModeChanged(bool value)
    {
        if (value) CloseBeforeAfterSplit();
        UpdateThumbnailPumpAdmission();
        if (value) CancelAdjacentPreviewWarm(true, dropRetained: true);
        CancelRestingPreview(clearParent: false);
        OnPropertyChanged(nameof(IsDevelopPreviewSurfaceActive));
        OnPropertyChanged(nameof(IsWorkspacePreviewSurfaceActive));
        OnPropertyChanged(nameof(IsFullScreenPreviewSurfaceActive));
        UpdateNavigatorPreviewSurfaceActivity();
        if (value)
        {
            ArmFullScreenSelection();
        }
        if (!value)
        {
            ReleaseFullScreenSelection();
        }

        CopyEditSettingsCommand.NotifyCanExecuteChanged();
        PasteEditSettingsCommand.NotifyCanExecuteChanged();
        NotifyWhiteBalanceCommandState();
        ToggleColorAssessmentModeCommand.NotifyCanExecuteChanged();
        ToggleBeforeAfterCommand.NotifyCanExecuteChanged();
        ToggleBeforeAfterSplitCommand.NotifyCanExecuteChanged();
        NotifyCaptureMemberStateChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        NotifyClippingCommandState();
        NotifyVersionCommandState();

        if (value && SelectedImage != null)
        {
            var generation = ReserveRenderOutcome();
            ApplySurfaceClearOutcome(SelectedImage, generation);
            LeaveDevelopClippingSurface();
            _ = LoadPreviewAsync(SelectedImage, generation);
        }
        else if (!value && IsDevelopMode &&
                 IsClippingOverlayLatched && SelectedImage != null)
        {
            RequestClippingOverlayRender();
        }
        else if (!value && IsBrowseMode)
        {
            var generation = ReserveRenderOutcome();
            ApplySurfaceClearOutcome(SelectedImage, generation);
            _previewLoadingCts?.Cancel();
            _previewDebounce?.Cancel();
            if (SelectedImage is { IsRaw: true } image &&
                image.EditSettings.HasEdits)
            {
                _ = TrackDirectThumbnailOperation(
                    RefreshThumbnailAsync(image));
            }
            ImageService.Previews.FlushRenderedPreviewCache();
            ScheduleHistogramUpdate();
        }
    }
}

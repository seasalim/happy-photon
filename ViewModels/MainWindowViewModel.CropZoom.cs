using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    internal const double ZoomStepFactor = 1.1;
    private bool _cropModeTransitionRequested;
    private bool _restoreCropModeOnRollback;

    private bool IsHistoryBlockedByCrop =>
        IsCropMode || _cropModeTransitionRequested;

    /// <summary>
    /// The crop to preview with. Crop mode renders the full corrected frame so
    /// the overlay's normalized coordinates line up with the displayed bitmap.
    /// </summary>
    private CropRegion? PreviewCrop() =>
        IsCropMode ? new CropRegion() : CurrentCrop;

    [ObservableProperty]
    private bool _isColorAssessmentMode;

    private bool CanToggleColorAssessmentMode() =>
        IsDevelopMode || IsFullScreenMode;

    [RelayCommand(CanExecute = nameof(CanToggleColorAssessmentMode))]
    private void ToggleColorAssessmentMode()
    {
        if (!CanToggleColorAssessmentMode()) return;

        IsColorAssessmentMode = !IsColorAssessmentMode;
        if (IsColorAssessmentMode)
        {
            ShowTransientStatus("Reference field is complete at Fit");
        }
    }

    private bool CanRotate() =>
        !IsHistoryBlockedByCrop && CanEditSelectedImage;

    [RelayCommand(CanExecute = nameof(CanRotate))]
    private void RotateLeft()
    {
        CommitRotation(-90, "Rotate left");
    }

    [RelayCommand(CanExecute = nameof(CanRotate))]
    private void RotateRight()
    {
        CommitRotation(90, "Rotate right");
    }

    private void CommitRotation(int delta, string label)
    {
        if (!CanEditSelectedImage || IsHistoryBlockedByCrop ||
            SelectedImage is not { } image)
            return;

        var before = CaptureLiveEditState();
        Rotation = (Rotation + delta + 360) % 360;
        image.EditSettings.Rotation = Rotation;
        var after = CaptureLiveEditState();
        var previousIntent = _requestedPreviewIntent;
        var generation = RequestEditedRender();
        _ = SaveEditSettingsCoreAsync(
            image, after, label, before, recordHistory: true,
            beforeSave: () => RenderCommittedGeometryAsync(
                image, before, generation, previousIntent));
        RefreshSelectedThumbnail();
    }

    private async Task<bool> RenderCommittedGeometryAsync(
        ImageFile image,
        EditSettings previousSettings,
        long generation,
        PreviewSurfaceIntent previousIntent)
    {
        var renderSucceeded = false;
        await UpdatePreviewWithCurrentSliders(
            generation: generation,
            observeRenderSucceeded: value => renderSucceeded = value);
        if (!renderSucceeded && generation == LatestPreviewOutcomeGeneration)
        {
            RollbackEditReservation(
                image, previousSettings, generation, previousIntent);
            return false;
        }
        return ReferenceEquals(SelectedImage, image);
    }

    /// <summary>
    /// Toggles crop mode on/off. When entering, initializes crop region.
    /// </summary>
    [RelayCommand]
    private async Task ToggleCropModeAsync()
    {
        if (!CanEditSelectedImage || SelectedImage == null) return;

        if (IsCropMode)
        {
            await CancelCropCoreAsync();
        }
        else
        {
            await EnterCropModeAsync();
        }
    }

    private async Task EnterCropModeAsync()
    {
        var image = SelectedImage;
        if (!CanEditSelectedImage || image == null) return;
        SetCropModeTransitionRequested(true);
        try
        {
            await WaitForPendingHistoryWorkAsync();
            if (!CanEditSelectedImage ||
                !ReferenceEquals(SelectedImage, image) || IsCropMode)
            {
                return;
            }
            CancelRestingPreview(clearParent: true);

            _cropBeforeEdit = image.EditSettings.Crop?.Clone();
            _horizonRotationBeforeEdit = image.EditSettings.HorizonRotation;

            CurrentCrop = image.EditSettings.Crop?.Clone() ?? new CropRegion();
            IsCropMode = true;

            ScheduleCropPreviewUpdate();
        }
        finally
        {
            SetCropModeTransitionRequested(false);
        }
    }

    /// <summary>
    /// Applies the current crop and exits crop mode.
    /// </summary>
    [RelayCommand]
    private async Task ApplyCropAsync()
    {
        var image = SelectedImage;
        SetCropModeTransitionRequested(true);
        try
        {
            await WaitForPendingHistoryWorkAsync();
            if (image == null || !CanEditSelectedImage || !IsCropMode ||
                !ReferenceEquals(SelectedImage, image) || CurrentCrop == null)
            {
                return;
            }

            var previousSettings = _lastSavedState?.Clone() ??
                image.EditSettings.Clone();
            var previousIntent = _requestedPreviewIntent;
            _previewDebounce?.Cancel();

            image.EditSettings.Crop = CurrentCrop.IsFullImage
                ? null
                : CurrentCrop.Clone();
            image.EditSettings.HorizonRotation = HorizonRotation;
            image.HasEdits = image.EditSettings.HasEdits;
            var appliedSettings = CaptureLiveEditState();

            _restoreCropModeOnRollback = true;
            IsCropMode = false;
            var generation = RequestEditedRender();
            try
            {
                await SaveEditSettingsCoreAsync(
                    image,
                    appliedSettings,
                    EditHistoryLabel.CropOperation(
                        previousSettings, appliedSettings),
                    previousSettings,
                    recordHistory: true,
                    beforeSave: () => RenderCommittedGeometryAsync(
                        image, previousSettings, generation, previousIntent));
            }
            catch
            {
                if (RollbackEditReservation(
                        image, previousSettings, generation, previousIntent))
                {
                    var rollbackGeneration = ReserveRenderOutcome(
                        PreviewSurfaceIntent.Edited,
                        promotionEligible: false);
                    await UpdatePreviewWithCurrentSliders(
                        generation: rollbackGeneration,
                        promotable: false);
                }
                throw;
            }
            if (IsCropMode) return;
            _restoreCropModeOnRollback = false;
            _cropBeforeEdit = null;
            RefreshSelectedThumbnail();
        }
        finally
        {
            _restoreCropModeOnRollback = false;
            SetCropModeTransitionRequested(false);
        }
    }

    /// <summary>
    /// Resets crop to full image while in crop mode.
    /// </summary>
    [RelayCommand]
    private void ResetCrop()
    {
        if (!CanEditSelectedImage) return;

        // Create new instance to trigger property change (modifying existing object won't update bindings)
        CurrentCrop = new CropRegion();
        HorizonRotation = 0.0;
        ScheduleCropPreviewUpdate();
    }

    /// <summary>
    /// Cancels crop mode and restores original crop.
    /// </summary>
    [RelayCommand]
    private Task CancelCropAsync() => CancelCropCoreAsync();

    private async Task CancelCropCoreAsync()
    {
        var image = SelectedImage;
        SetCropModeTransitionRequested(true);
        try
        {
            await WaitForPendingHistoryWorkAsync();
            if (!IsCropMode || !ReferenceEquals(SelectedImage, image)) return;

            CurrentCrop = _cropBeforeEdit?.Clone();
            HorizonRotation = _horizonRotationBeforeEdit;
            IsCropMode = false;
            _cropBeforeEdit = null;
            ScheduleCropPreviewUpdate();
        }
        finally
        {
            SetCropModeTransitionRequested(false);
        }
    }

    private void SetCropModeTransitionRequested(bool value)
    {
        if (_cropModeTransitionRequested == value) return;
        _cropModeTransitionRequested = value;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        JumpToHistoryStepCommand.NotifyCanExecuteChanged();
        ClearHistoryCommand.NotifyCanExecuteChanged();
        ClearHistoryAboveStepCommand.NotifyCanExecuteChanged();
        RotateLeftCommand.NotifyCanExecuteChanged();
        RotateRightCommand.NotifyCanExecuteChanged();
    }

    private void RefreshSelectedThumbnail()
    {
        // Async command continuations can resume after disposal's activity
        // drain; the closed channel keeps them from arming fresh work.
        if (_renderOutcomeChannelClosed) return;
        var image = SelectedImage;
        if (image == null) return;
        var refresh = ReplaceDebounce(ref _thumbnailDebounce);
        _ = TrackDirectThumbnailOperation(
            RefreshSelectedThumbnailAsync(image, refresh.Token));
    }

    private void ScheduleThumbnailRefresh()
    {
        if (_renderOutcomeChannelClosed) return;
        var image = SelectedImage;
        if (image == null) return;
        var debounce = ReplaceDebounce(ref _thumbnailDebounce);
        _ = DebouncedAction.RunAsync(
            "thumbnail refresh",
            TimeSpan.FromMilliseconds(500),
            debounce.Token,
            // Cancellation cannot stop a fired delay whose continuation is
            // still queued; re-checking disposal (channel closes first, on
            // this thread) stops work the activity drain could not see.
            () => _renderOutcomeChannelClosed
                ? Task.CompletedTask
                : TrackDirectThumbnailOperation(
                    RefreshSelectedThumbnailAsync(image, debounce.Token)),
            timeProvider: _timeProvider);
    }

    private async Task RefreshSelectedThumbnailAsync(
        ImageFile image,
        CancellationToken cancellationToken)
    {
        var sizeGeneration = Volatile.Read(ref _thumbnailSizeGeneration);
        using var result = await ImageService.LoadThumbnailAsync(
            image,
            BrowseThumbnailRequest,
            cancellationToken);
        if (cancellationToken.IsCancellationRequested ||
            sizeGeneration != Volatile.Read(ref _thumbnailSizeGeneration))
        {
            return;
        }

        if (ReferenceEquals(SelectedImage, image) && Browse.Contains(image))
        {
            ApplyThumbnailLoadResult(image, result);
            if (result.Status == ThumbnailLoadStatus.Loaded)
            {
                Browse.ReplaceThumbnail(image, result.DetachBitmap());
                UpdateThumbnailMemoryDiagnostics();
            }
        }
    }

    public void AdjustZoom(double delta)
    {
        if (delta > 0)
            ApplyManualZoom(ZoomLevel * ZoomStepFactor);
        else
            ApplyManualZoom(ZoomLevel / ZoomStepFactor);
    }

    // Folder Tree Methods
}

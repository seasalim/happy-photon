using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
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

    [RelayCommand]
    private void RotateLeft()
    {
        if (!CanEditSelectedImage || SelectedImage == null) return;
        
        // Rotate counter-clockwise (subtract 90, wrap around)
        // Note: Rotation is separate from undo/reset - it's a geometric transform
        Rotation = (Rotation - 90 + 360) % 360;
        SelectedImage.EditSettings.Rotation = Rotation;
        
        SchedulePreviewUpdate(pushUndo: false);
        
        // Refresh thumbnail with new rotation
        RefreshSelectedThumbnail();
    }

    [RelayCommand]
    private void RotateRight()
    {
        if (!CanEditSelectedImage || SelectedImage == null) return;

        // Rotate clockwise (add 90, wrap around)
        // Note: Rotation is separate from undo/reset - it's a geometric transform
        Rotation = (Rotation + 90) % 360;
        SelectedImage.EditSettings.Rotation = Rotation;

        SchedulePreviewUpdate(pushUndo: false);

        // Refresh thumbnail with new rotation
        RefreshSelectedThumbnail();
    }

    /// <summary>
    /// Toggles crop mode on/off. When entering, initializes crop region.
    /// </summary>
    [RelayCommand]
    private void ToggleCropMode()
    {
        if (!CanEditSelectedImage || SelectedImage == null) return;

        if (IsCropMode)
        {
            // Exiting crop mode without applying - restore original
            CancelCrop();
        }
        else
        {
            // Entering crop mode
            EnterCropMode();
        }
    }

    private void EnterCropMode()
    {
        if (!CanEditSelectedImage || SelectedImage == null) return;
        CancelRestingPreview(clearParent: true);

        // Save original crop for cancel
        _cropBeforeEdit = SelectedImage.EditSettings.Crop?.Clone();
        _horizonRotationBeforeEdit = SelectedImage.EditSettings.HorizonRotation;

        // Initialize CurrentCrop - use existing or create new full-image region
        CurrentCrop = SelectedImage.EditSettings.Crop?.Clone() ?? new CropRegion();
        IsCropMode = true;

        // Refresh preview to show uncropped image (UpdatePreviewWithCurrentSliders
        // will skip crop when IsCropMode is true)
        SchedulePreviewUpdate(pushUndo: false);
    }

    /// <summary>
    /// Applies the current crop and exits crop mode.
    /// </summary>
    [RelayCommand]
    private async Task ApplyCropAsync()
    {
        if (!CanEditSelectedImage ||
            SelectedImage == null ||
            CurrentCrop == null)
        {
            IsCropMode = false;
            return;
        }

        var image = SelectedImage;
        var previousSettings = image.EditSettings.Clone();
        var previousIntent = _requestedPreviewIntent;
        _previewDebounce?.Cancel();
        var generation = RequestEditedRender();

        // Apply crop to settings
        if (CurrentCrop.IsFullImage)
        {
            image.EditSettings.Crop = null;
        }
        else
        {
            image.EditSettings.Crop = CurrentCrop.Clone();
        }
        image.EditSettings.HorizonRotation = HorizonRotation;

        image.HasEdits = image.EditSettings.HasEdits;

        // Save to catalog
        try
        {
            await SaveEditSettingsAsync(image);
        }
        catch
        {
            RollbackCropReservation(
                image,
                previousSettings,
                generation,
                previousIntent);
            throw;
        }
        _lastSavedState = image.EditSettings.Clone();

        // Exit crop mode first, so preview update shows cropped result
        IsCropMode = false;
        _cropBeforeEdit = null;

        // Update preview and thumbnail (no undo for crop - it's a geometric transform)
        if (await UpdatePreviewWithCurrentSliders(generation: generation))
        {
            RefreshSelectedThumbnail();
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
    private void CancelCrop()
    {
        // Restore original crop
        CurrentCrop = _cropBeforeEdit?.Clone();
        HorizonRotation = _horizonRotationBeforeEdit;

        IsCropMode = false;
        _cropBeforeEdit = null;

        // Refresh preview to show image with original crop applied
        SchedulePreviewUpdate(pushUndo: false);
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
            LibraryThumbnailRequest,
            cancellationToken);
        if (cancellationToken.IsCancellationRequested ||
            sizeGeneration != Volatile.Read(ref _thumbnailSizeGeneration))
        {
            return;
        }

        if (ReferenceEquals(SelectedImage, image) && Library.Contains(image))
        {
            ApplyThumbnailLoadResult(image, result);
            if (result.Status == ThumbnailLoadStatus.Loaded)
            {
                Library.ReplaceThumbnail(image, result.DetachBitmap());
                UpdateThumbnailMemoryDiagnostics();
            }
        }
    }

    public void AdjustZoom(double delta)
    {
        if (delta > 0)
            ApplyManualZoom(ZoomLevel * 1.1);
        else
            ApplyManualZoom(ZoomLevel / 1.1);
    }

    // Folder Tree Methods
}

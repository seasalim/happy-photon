using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanUndoEdit))]
    private async Task UndoAsync()
    {
        if (!CanUndoEdit()) return;
        if (SelectedImage == null) return;

        var target = _history.Undo(CaptureLiveEditState());
        if (target == null) return;
        SyncHistoryFlags();
        await ApplyHistoryStateAsync(target);
    }

    [RelayCommand(CanExecute = nameof(CanRedoEdit))]
    private async Task RedoAsync()
    {
        if (!CanRedoEdit()) return;
        if (SelectedImage == null) return;

        var target = _history.Redo(CaptureLiveEditState());
        if (target == null) return;
        SyncHistoryFlags();
        await ApplyHistoryStateAsync(target);
    }

    private async Task ApplyHistoryStateAsync(EditSettings state)
    {
        _isLoadingImage = true;
        LoadSlidersFrom(state);
        // Preserve current geometric transforms.
        Rotation = SelectedImage!.EditSettings.Rotation;
        HorizonRotation = SelectedImage.EditSettings.HorizonRotation;
        CurrentCrop = SelectedImage.EditSettings.Crop?.Clone();
        _isLoadingImage = false;

        EditSettingsTransfer.ApplySubset(state, SelectedImage.EditSettings);
        SelectedImage.EditSettings.AppliedPresetId = ActivePresetId;
        LoadCurrentCurveFrom(SelectedImage.EditSettings);
        SelectedImage.HasEdits = SelectedImage.EditSettings.HasEdits;
        await SaveEditSettingsAsync(SelectedImage);
        _lastSavedState = SelectedImage.EditSettings.Clone();

        await UpdatePreviewWithCurrentSliders();
        UpdateCanReset();
        RefreshSelectedThumbnail();
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task ResetEditsAsync()
    {
        if (!CanEditSelectedImage || SelectedImage == null) return;

        PushLiveUndoState();

        // Preserve rotation and crop - Reset only affects color/tonal adjustments
        var currentRotation = Rotation;
        var currentHorizonRotation = HorizonRotation;
        var currentCrop = CurrentCrop;

        // Reset color/tonal adjustments (not rotation or crop)
        SelectedImage.EditSettings.Exposure = 0;
        SelectedImage.EditSettings.Wb = new WhiteBalanceSettings();
        SelectedImage.EditSettings.Brightness = 0;
        SelectedImage.EditSettings.Contrast = 0;
        SelectedImage.EditSettings.Saturation = 0;
        SelectedImage.EditSettings.Vibrance = 0;
        SelectedImage.EditSettings.Shadows = 0;
        SelectedImage.EditSettings.Highlights = 0;
        SelectedImage.EditSettings.BaseLook = null;
        SelectedImage.EditSettings.HlReconstruction = HlReconstructionMode.Clip;
        SelectedImage.EditSettings.Detail = new DetailSettings();
        SelectedImage.EditSettings.Curve.Reset();
        SelectedImage.EditSettings.CurveRed = null;
        SelectedImage.EditSettings.CurveGreen = null;
        SelectedImage.EditSettings.CurveBlue = null;
        SelectedImage.EditSettings.AppliedPresetId = null;
        // Note: Rotation, horizon rotation, and crop are preserved (geometric transforms)
        SelectedImage.HasEdits = SelectedImage.EditSettings.HasEdits;

        // Prevent spurious preview updates while resetting sliders
        _isLoadingImage = true;
        Exposure = 0;
        LoadWhiteBalanceFrom(SelectedImage.EditSettings);
        LoadHighlightReconstructionFrom(SelectedImage.EditSettings);
        LoadDetailFrom(SelectedImage.EditSettings);
        Brightness = 0;
        Contrast = 0;
        Saturation = 0;
        Vibrance = 0;
        Shadows = 0;
        Highlights = 0;
        Rotation = currentRotation;
        HorizonRotation = currentHorizonRotation;
        CurrentCrop = currentCrop;
        LoadCurrentCurveFrom(SelectedImage.EditSettings);
        ActivePresetId = null;
        _isLoadingImage = false;

        // Save to catalog (geometric transforms are preserved)
        await SaveEditSettingsAsync(SelectedImage);

        // Update last saved state
        _lastSavedState = SelectedImage.EditSettings.Clone();

        // Trigger live preview update
        await UpdatePreviewWithCurrentSliders();
        UpdateCanReset();

        // Refresh thumbnail to reflect reset
        RefreshSelectedThumbnail();
    }

    /// <summary>
    /// Applies a preset to the current image, or untoggles if the same preset is already active.
    /// </summary>
    public async Task ApplyPresetAsync(string presetId)
    {
        if (!CanEditSelectedImage || SelectedImage == null) return;

        // Clear hover state - we're committing to this preset
        _isHoveringPreset = false;
        _preHoverSettings = null;
        _hoverPreviewCts?.Cancel();

        // If same preset is active, untoggle (reset)
        if (ActivePresetId == presetId)
        {
            await ResetEditsAsync();
            return;
        }

        var preset = PresetService.GetById(presetId);
        if (preset == null) return;

        // Push current state to undo stack before applying preset
        PushUndoState();
        var currentCrop = CurrentCrop?.Clone();

        _isLoadingImage = true;
        LoadSlidersFrom(preset.Settings);
        ActivePresetId = presetId;
        Rotation = SelectedImage.EditSettings.Rotation;
        HorizonRotation = SelectedImage.EditSettings.HorizonRotation;
        CurrentCrop = currentCrop;
        _isLoadingImage = false;

        EditSettingsTransfer.ApplySubset(preset.Settings, SelectedImage.EditSettings);
        SelectedImage.EditSettings.AppliedPresetId = presetId;
        LoadCurrentCurveFrom(SelectedImage.EditSettings);
        SelectedImage.HasEdits = true;

        // Save to catalog
        await SaveEditSettingsAsync(SelectedImage);
        _lastSavedState = SelectedImage.EditSettings.Clone();

        // Update preview and UI
        await UpdatePreviewWithCurrentSliders();
        UpdateCanReset();

        // Refresh thumbnail to reflect preset
        RefreshSelectedThumbnail();
    }

    /// <summary>
    /// Shows a temporary preview of a preset on hover without applying it.
    /// </summary>
    public async Task PreviewPresetHoverAsync(string presetId)
    {
        if (SelectedImage == null) return;
        var image = SelectedImage;
        CancelRestingPreview(clearParent: true);

        _hoverPreviewCts?.Cancel();
        _hoverPreviewCts = new CancellationTokenSource();
        var token = _hoverPreviewCts.Token;

        var preset = PresetService.GetById(presetId);
        if (preset == null) return;

        if (!_isHoveringPreset)
        {
            _preHoverSettings = SelectedImage.EditSettings.Clone();
            SaveSlidersTo(_preHoverSettings);
            _preHoverSettings.Crop = CurrentCrop?.Clone();
            _isHoveringPreset = true;
        }

        try
        {
            var previewSettings = EditSettingsTransfer.CopySubset(preset.Settings);
            previewSettings.Rotation = Rotation;
            previewSettings.HorizonRotation = HorizonRotation;
            previewSettings.Crop = PreviewCrop();

            using var artifacts = await ImageService.ApplyEditsToPreviewArtifactsAsync(
                image,
                previewSettings,
                LibraryThumbnailRequest,
                skipHistogram: true,
                RequestedClippingOverlaySides,
                token);
            var preview = artifacts.DetachBitmap();

            if (!token.IsCancellationRequested && preview != null &&
                ReferenceEquals(SelectedImage, image) &&
                (IsDevelopMode || IsFullScreenMode))
            {
                IsShowingOriginal = false;
                InstallPreviewClipping(artifacts);
                ReplacePreviewImage(preview, PreviewPaintSource.FreshRender);
            }
            else
            {
                preview?.Dispose();
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Restores the preview to the pre-hover state.
    /// </summary>
    public async Task RestoreFromHoverAsync()
    {
        if (!_isHoveringPreset || SelectedImage == null) return;
        var image = SelectedImage;

        _hoverPreviewCts?.Cancel();
        _isHoveringPreset = false;

        if (_preHoverSettings is { } preHoverSettings)
        {
            _preHoverSettings = null;
            try
            {
                using var artifacts = await ImageService.ApplyEditsToPreviewArtifactsAsync(
                    image,
                    preHoverSettings,
                    LibraryThumbnailRequest,
                    skipHistogram: true,
                    RequestedClippingOverlaySides);
                var preview = artifacts.DetachBitmap();

                if (preview != null && ReferenceEquals(SelectedImage, image) &&
                    (IsDevelopMode || IsFullScreenMode))
                {
                    IsShowingOriginal = false;
                    InstallPreviewClipping(artifacts);
                    ReplacePreviewImage(preview, PreviewPaintSource.FreshRender);
                    OnAcceptedInteractivePreview(preview);
                }
                else
                {
                    preview?.Dispose();
                }
            }
            catch (OperationCanceledException) { }
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleBeforeAfter))]
    private async Task ToggleBeforeAfterAsync()
    {
        if (!CanToggleBeforeAfter() || SelectedImage == null) return;

        var image = SelectedImage;
        CancelRestingPreview(clearParent: true);
        IsShowingOriginal = !IsShowingOriginal;

        if (IsShowingOriginal)
        {
            // Create temporary settings with only rotation and crop (no color edits)
            var tempSettings = new EditSettings
            {
                Rotation = Rotation,
                HorizonRotation = HorizonRotation,
                // In crop mode, show the full canvas so the overlay stays aligned
                Crop = PreviewCrop(),
                Curve = new CurveData()
            };

            // Show original preview without any edits (same size as edited preview)
            using var artifacts = await ImageService.ApplyEditsToPreviewArtifactsAsync(
                image,
                tempSettings,
                LibraryThumbnailRequest,
                skipHistogram: false,
                RequestedClippingOverlaySides);
            var preview = artifacts.DetachBitmap();
            if (preview == null ||
                !CanUseBeforeAfterWorkspace() ||
                !ReferenceEquals(SelectedImage, image) ||
                !IsShowingOriginal)
            {
                preview?.Dispose();
                if (!CanUseBeforeAfterWorkspace())
                {
                    IsShowingOriginal = false;
                }
                return;
            }
            InstallPreviewClipping(artifacts);
            ReplacePreviewImage(preview, PreviewPaintSource.FreshRender);
            Histogram = artifacts.Histogram;
        }
        else
        {
            // Show edited image
            await UpdatePreviewWithCurrentSliders();
        }
    }

    private bool CanUndoEdit() =>
        CanUndo && IsDevelopMode && !IsFullScreenMode && CanEditSelectedImage;

    private bool CanRedoEdit() =>
        CanRedo && IsDevelopMode && !IsFullScreenMode && CanEditSelectedImage;

    private bool CanToggleBeforeAfter() =>
        CanReset && CanEditSelectedImage && CanUseBeforeAfterWorkspace();

    private bool CanUseBeforeAfterWorkspace() =>
        IsDevelopMode || IsFullScreenMode;
}

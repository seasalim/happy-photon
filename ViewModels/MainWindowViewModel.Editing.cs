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
        _previewDebounce?.Cancel();
        var image = SelectedImage!;
        var previousSettings = CaptureLiveEditState();
        var previousIntent = _requestedPreviewIntent;
        var generation = RequestEditedRender();
        _isLoadingImage = true;
        LoadSlidersFrom(state);
        // Rotation, horizon, and crop stay outside edit history.
        Rotation = SelectedImage!.EditSettings.Rotation;
        HorizonRotation = SelectedImage.EditSettings.HorizonRotation;
        CurrentCrop = SelectedImage.EditSettings.Crop?.Clone();
        _isLoadingImage = false;

        EditSettingsTransfer.ApplySubset(state, SelectedImage.EditSettings);
        SelectedImage.EditSettings.Geometry = state.Geometry?.Clone();
        WriteRawProfileSelection(SelectedImage, state.RawProfile);
        SelectedImage.EditSettings.AppliedPresetId = ActivePresetId;
        LoadCurrentCurveFrom(SelectedImage.EditSettings);
        SelectedImage.HasEdits = SelectedImage.EditSettings.HasEdits;
        try
        {
            await SaveEditSettingsAsync(SelectedImage);
        }
        catch
        {
            RollbackEditReservation(
                image,
                previousSettings,
                generation,
                previousIntent);
            throw;
        }
        _lastSavedState = SelectedImage.EditSettings.Clone();

        await UpdatePreviewWithCurrentSliders(generation: generation);
        UpdateCanReset();
        RefreshSelectedThumbnail();
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private Task ResetEditsAsync() => ResetEditsCoreAsync(
        preserveProfile: false);

    private async Task ResetEditsCoreAsync(bool preserveProfile)
    {
        if (!CanEditSelectedImage || SelectedImage == null) return;
        var image = SelectedImage;
        var previousSettings = CaptureLiveEditState();
        var previousIntent = _requestedPreviewIntent;
        var generation = RequestEditedRender();

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
        SelectedImage.EditSettings.Effects = null;
        SelectedImage.EditSettings.Mixer = null;
        SelectedImage.EditSettings.Lens.RestoreBaseline();
        SelectedImage.EditSettings.Geometry = null;
        SelectedImage.EditSettings.Curve.Reset();
        SelectedImage.EditSettings.CurveRed = null;
        SelectedImage.EditSettings.CurveGreen = null;
        SelectedImage.EditSettings.CurveBlue = null;
        SelectedImage.EditSettings.AppliedPresetId = null;
        WriteRawProfileSelection(
            SelectedImage,
            preserveProfile ? SelectedImage.EditSettings.RawProfile : null);
        // Note: Rotation, horizon rotation, and crop are preserved (geometric transforms)
        SelectedImage.HasEdits = SelectedImage.EditSettings.HasEdits;

        // Prevent spurious preview updates while resetting sliders
        _isLoadingImage = true;
        Exposure = 0;
        LoadWhiteBalanceFrom(SelectedImage.EditSettings);
        LoadHighlightReconstructionFrom(SelectedImage.EditSettings);
        LoadDetailFrom(SelectedImage.EditSettings);
        LoadEffectsFrom(SelectedImage.EditSettings);
        LoadMixerFrom(SelectedImage.EditSettings);
        LoadLensFrom(SelectedImage.EditSettings);
        ResetGeometryUi();
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
        try
        {
            await SaveEditSettingsAsync(SelectedImage);
        }
        catch
        {
            RollbackEditReservation(
                image,
                previousSettings,
                generation,
                previousIntent);
            throw;
        }

        // Update last saved state
        _lastSavedState = SelectedImage.EditSettings.Clone();

        // Trigger live preview update
        await UpdatePreviewWithCurrentSliders(generation: generation);
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
        var image = SelectedImage;

        // Clear hover state - we're committing to this preset
        _isHoveringPreset = false;
        _preHoverSettings = null;
        _hoverPreviewCts?.Cancel();

        // If same preset is active, untoggle (reset)
        if (ActivePresetId == presetId)
        {
            await UntogglePresetAsync();
            return;
        }

        var preset = PresetService.GetById(presetId);
        if (preset == null) return;
        var previousSettings = CaptureLiveEditState();
        var previousIntent = _requestedPreviewIntent;
        var generation = RequestEditedRender();

        // Push current state to undo stack before applying preset
        PushUndoState();
        var currentCrop = CurrentCrop?.Clone();
        var currentGeometry = previousSettings.Geometry?.Clone();

        _isLoadingImage = true;
        LoadSlidersFrom(preset.Settings);
        ActivePresetId = presetId;
        Rotation = SelectedImage.EditSettings.Rotation;
        HorizonRotation = SelectedImage.EditSettings.HorizonRotation;
        LoadGeometryFrom(previousSettings);
        CurrentCrop = currentCrop;
        _isLoadingImage = false;

        EditSettingsTransfer.ApplySubset(preset.Settings, SelectedImage.EditSettings);
        SelectedImage.EditSettings.Geometry = currentGeometry;
        SelectedImage.EditSettings.AppliedPresetId = presetId;
        LoadCurrentCurveFrom(SelectedImage.EditSettings);
        SelectedImage.HasEdits = true;

        // Save to catalog
        try
        {
            await SaveEditSettingsAsync(SelectedImage);
        }
        catch
        {
            RollbackEditReservation(
                image,
                previousSettings,
                generation,
                previousIntent);
            throw;
        }
        _lastSavedState = SelectedImage.EditSettings.Clone();

        // Update preview and UI
        await UpdatePreviewWithCurrentSliders(generation: generation);
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
        var generation = ReserveRenderOutcome(
            PreviewSurfaceIntent.Edited,
            promotionEligible: false);

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
            previewSettings.RawProfile = image.EditSettings.RawProfile?.Clone();
            previewSettings.Rotation = Rotation;
            previewSettings.HorizonRotation = HorizonRotation;
            previewSettings.Crop = PreviewCrop();
            previewSettings.Geometry = image.EditSettings.Geometry?.Clone();

            using var artifacts = await ImageService.Previews.ApplyEditsToPreviewArtifactsAsync(
                image,
                previewSettings,
                BrowseThumbnailRequest,
                skipHistogram: false,
                RequestedClippingOverlaySides,
                token,
                generation,
                computeWaveform: IsWaveformScopeEffective);
            token.ThrowIfCancellationRequested();
            ApplyRenderOutcome(RenderOutcome.FromArtifacts(
                image,
                generation,
                PreviewSurfaceIntent.Edited,
                RenderOutcomeClass.StateDefining,
                PreviewPaintSource.FreshRender,
                artifacts,
                promotable: false));
        }
        catch (OperationCanceledException) { }
    }

    private async Task UntogglePresetAsync()
    {
        await ResetEditsCoreAsync(preserveProfile: true);
    }

    /// <summary>
    /// Restores the preview to the pre-hover state.
    /// </summary>
    public async Task RestoreFromHoverAsync()
    {
        if (!_isHoveringPreset || SelectedImage == null) return;
        var image = SelectedImage;
        var generation = RequestEditedRender();

        _hoverPreviewCts?.Cancel();
        _isHoveringPreset = false;

        if (_preHoverSettings is { } preHoverSettings)
        {
            _preHoverSettings = null;
            try
            {
                using var artifacts = await ImageService.Previews.ApplyEditsToPreviewArtifactsAsync(
                    image,
                    preHoverSettings,
                    BrowseThumbnailRequest,
                    skipHistogram: false,
                    RequestedClippingOverlaySides,
                    surfaceGeneration: generation,
                    computeWaveform: IsWaveformScopeEffective);
                ApplyRenderOutcome(RenderOutcome.FromArtifacts(
                    image,
                    generation,
                    PreviewSurfaceIntent.Edited,
                    RenderOutcomeClass.StateDefining,
                    PreviewPaintSource.FreshRender,
                    artifacts,
                    promotable: true));
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
        var previousIntent = _requestedPreviewIntent;
        var intent = _requestedPreviewIntent == PreviewSurfaceIntent.Original
            ? PreviewSurfaceIntent.Edited
            : PreviewSurfaceIntent.Original;
        var generation = ReserveRenderOutcome(
            intent,
            promotionEligible: false);

        if (intent == PreviewSurfaceIntent.Original)
        {
            var tempSettings = BuildOriginalRenderSettings(image);

            // Show original preview without any edits (same size as edited preview)
            using var artifacts = await ImageService.Previews.ApplyEditsToPreviewArtifactsAsync(
                image,
                tempSettings,
                BrowseThumbnailRequest,
                skipHistogram: false,
                RequestedClippingOverlaySides,
                surfaceGeneration: generation,
                computeWaveform: IsWaveformScopeEffective);
            ApplyRenderOutcome(RenderOutcome.FromArtifacts(
                image,
                generation,
                PreviewSurfaceIntent.Original,
                RenderOutcomeClass.StateDefining,
                PreviewPaintSource.FreshRender,
                artifacts,
                promotable: false,
                rollbackRequestedIntent: previousIntent));
        }
        else
        {
            // Show edited image
            await UpdatePreviewWithCurrentSliders(
                generation: generation,
                rollbackRequestedIntent: previousIntent);
        }
    }

    // Original-surface settings keep geometry-like decode state (rotation,
    // horizon, crop, lens) while dropping color edits; the full-canvas crop keeps
    // any overlay aligned. Shared so the clipping overlay renders the exact frame
    // Before/After paints, never edited settings mistaken for the original.
    private EditSettings BuildOriginalRenderSettings(ImageFile image) => new()
    {
        Rotation = Rotation,
        HorizonRotation = HorizonRotation,
        Crop = PreviewCrop(),
        Geometry = image.EditSettings.Geometry?.Clone(),
        Curve = new CurveData(),
        Lens = image.EditSettings.Lens.Clone()
    };

    private bool CanUndoEdit() =>
        CanUndo && IsDevelopMode && !IsFullScreenMode && CanEditSelectedImage;

    private bool CanRedoEdit() =>
        CanRedo && IsDevelopMode && !IsFullScreenMode && CanEditSelectedImage;

    private bool CanToggleBeforeAfter() =>
        CanReset && CanEditSelectedImage && CanUseBeforeAfterWorkspace();

    private bool CanUseBeforeAfterWorkspace() =>
        IsDevelopMode || IsFullScreenMode;
}

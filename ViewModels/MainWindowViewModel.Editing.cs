using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (IsFullScreenMode) return;
        if (SelectedImage == null) return;

        var target = _history.Undo(CaptureLiveEditState());
        if (target == null) return;
        SyncHistoryFlags();
        await ApplyHistoryStateAsync(target);
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private async Task RedoAsync()
    {
        if (IsFullScreenMode) return;
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

        SaveSlidersTo(SelectedImage.EditSettings);
        SelectedImage.HasEdits = SelectedImage.EditSettings.HasEdits;
        await SaveEditSettingsAsync(SelectedImage);
        _lastSavedState = SelectedImage.EditSettings.Clone();

        await UpdatePreviewWithCurrentSliders();
        UpdateCanReset();
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task ResetEditsAsync()
    {
        if (SelectedImage == null) return;

        // Clear history - reset is not undoable
        _history.Clear();
        SyncHistoryFlags();

        // Preserve rotation and crop - Reset only affects color/tonal adjustments
        var currentRotation = Rotation;
        var currentHorizonRotation = HorizonRotation;
        var currentCrop = CurrentCrop;

        // Reset color/tonal adjustments (not rotation or crop)
        SelectedImage.EditSettings.Exposure = 0;
        SelectedImage.EditSettings.Temperature = 0;
        SelectedImage.EditSettings.Brightness = 0;
        SelectedImage.EditSettings.Contrast = 0;
        SelectedImage.EditSettings.Saturation = 0;
        SelectedImage.EditSettings.Vibrance = 0;
        SelectedImage.EditSettings.Shadows = 0;
        SelectedImage.EditSettings.Highlights = 0;
        SelectedImage.EditSettings.Curve.Reset();
        SelectedImage.EditSettings.AppliedPresetId = null;
        // Note: Rotation, horizon rotation, and crop are preserved (geometric transforms)
        SelectedImage.HasEdits = SelectedImage.EditSettings.HasEdits;

        // Prevent spurious preview updates while resetting sliders
        _isLoadingImage = true;
        Exposure = 0;
        Temperature = 0;
        Brightness = 0;
        Contrast = 0;
        Saturation = 0;
        Vibrance = 0;
        Shadows = 0;
        Highlights = 0;
        Rotation = currentRotation;
        HorizonRotation = currentHorizonRotation;
        CurrentCrop = currentCrop;
        CurrentCurve = new CurveData();
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
        _ = RefreshSelectedThumbnailAsync();
    }

    /// <summary>
    /// Applies a preset to the current image, or untoggles if the same preset is already active.
    /// </summary>
    public async Task ApplyPresetAsync(string presetId)
    {
        if (SelectedImage == null) return;

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
        CurrentCurve = preset.Settings.Curve.Clone();
        ActivePresetId = presetId;
        Rotation = SelectedImage.EditSettings.Rotation;
        HorizonRotation = SelectedImage.EditSettings.HorizonRotation;
        CurrentCrop = currentCrop;
        _isLoadingImage = false;

        SaveSlidersTo(SelectedImage.EditSettings);
        SelectedImage.HasEdits = true;

        // Save to catalog
        await SaveEditSettingsAsync(SelectedImage);
        _lastSavedState = SelectedImage.EditSettings.Clone();

        // Update preview and UI
        await UpdatePreviewWithCurrentSliders();
        UpdateCanReset();

        // Refresh thumbnail to reflect preset
        _ = RefreshSelectedThumbnailAsync();
    }

    /// <summary>
    /// Shows a temporary preview of a preset on hover without applying it.
    /// </summary>
    public async Task PreviewPresetHoverAsync(string presetId)
    {
        if (SelectedImage == null) return;

        _hoverPreviewCts?.Cancel();
        _hoverPreviewCts = new CancellationTokenSource();
        var token = _hoverPreviewCts.Token;

        var preset = PresetService.GetById(presetId);
        if (preset == null) return;

        if (!_isHoveringPreset)
        {
            _preHoverSettings = new EditSettings();
            SaveSlidersTo(_preHoverSettings);
            _preHoverSettings.Crop = CurrentCrop?.Clone();
            _isHoveringPreset = true;
        }

        try
        {
            // Merge preset settings with current geometric transforms (Rotation, Crop)
            var previewSettings = new EditSettings
            {
                Exposure = preset.Settings.Exposure,
                Temperature = preset.Settings.Temperature,
                Brightness = preset.Settings.Brightness,
                Contrast = preset.Settings.Contrast,
                Saturation = preset.Settings.Saturation,
                Vibrance = preset.Settings.Vibrance,
                Shadows = preset.Settings.Shadows,
                Highlights = preset.Settings.Highlights,
                Rotation = Rotation,  // Preserve current rotation
                HorizonRotation = HorizonRotation,
                Crop = IsCropMode ? null : CurrentCrop,  // Preserve current crop (unless in crop mode)
                Curve = preset.Settings.Curve.Clone()
            };

            var (preview, _) = await ImageService.ApplyEditsToPreviewAsync(
                SelectedImage, previewSettings, skipHistogram: true, token);

            if (!token.IsCancellationRequested && preview != null)
            {
                ReplacePreviewImage(preview);
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

        _hoverPreviewCts?.Cancel();
        _isHoveringPreset = false;

        if (_preHoverSettings != null)
        {
            try
            {
                var (preview, _) = await ImageService.ApplyEditsToPreviewAsync(
                    SelectedImage, _preHoverSettings, skipHistogram: true);

                if (preview != null)
                {
                    ReplacePreviewImage(preview);
                }
            }
            catch (OperationCanceledException) { }

            _preHoverSettings = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task ToggleBeforeAfterAsync()
    {
        if (SelectedImage == null) return;

        IsShowingOriginal = !IsShowingOriginal;

        if (IsShowingOriginal)
        {
            // Create temporary settings with only rotation and crop (no color edits)
            var tempSettings = new EditSettings
            {
                Rotation = Rotation,
                HorizonRotation = HorizonRotation,
                // In crop mode, show uncropped image so user can see full image with overlay
                Crop = IsCropMode ? null : CurrentCrop,
                Curve = new CurveData()
            };

            // Show original preview without any edits (same size as edited preview)
            var (preview, histogram) = await ImageService.ApplyEditsToPreviewAsync(
                SelectedImage, tempSettings, skipHistogram: false);
            ReplacePreviewImage(preview);
            Histogram = histogram;
        }
        else
        {
            // Show edited image
            await UpdatePreviewWithCurrentSliders();
        }
    }
}

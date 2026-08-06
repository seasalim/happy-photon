using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnSelectedImageChanged(ImageFile? oldValue, ImageFile? newValue)
    {
        // Update IsActive flags for visual highlighting in Library grid
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null) newValue.IsActive = true;

        HasSelectedImage = newValue != null;
        IsShowingOriginal = false;
        Volatile.Write(ref _activeBaseRefreshRequestId, 0);
        IsBaseArming = false;
        OnPropertyChanged(nameof(ActiveFileName));

        // Exit crop mode when switching images
        if (IsCropMode)
        {
            IsCropMode = false;
            _cropBeforeEdit = null;
            _horizonRotationBeforeEdit = 0.0;
        }

        // Clear undo/redo history when switching images
        _history.Clear();
        SyncHistoryFlags();

        // Prevent slider changes from triggering preview updates while loading
        _isLoadingImage = true;

        if (newValue != null)
        {
            PrepareWhiteBalanceUi(newValue);
            LoadSlidersFrom(newValue.EditSettings);
            _lastSavedState = newValue.EditSettings.Clone();

            _isLoadingImage = false;

            if (IsDevelopMode || IsFullScreenMode)
            {
                _ = LoadPreviewAsync(newValue);
            }
            else
            {
                ScheduleHistogramUpdate();
            }

            // Load Metadata
            _ = ImageService.LoadMetadataAsync(newValue);
            UpdateCanReset();
        }
        else
        {
            // Clear cached preview when no image is selected
            ImageService.ClearPreviewCache();

            ReplacePreviewImage(null);
            Histogram = null;
            CurrentCrop = null;
            CurrentCurve = null;
            ActivePresetId = null;
            IsFullScreenMode = false;
            _lastSavedState = null;
            ResetSliders();
            _isLoadingImage = false;
        }
    }

    private void OnLibraryFilterChanged(object? sender, EventArgs e)
    {
        if (!Library.ContainsVisible(SelectedImage))
        {
            SelectedImage = Library.FirstVisible();
        }
        else if (SelectedImage == null)
        {
            SelectedImage = Library.FirstVisible();
        }

        UpdateSelectedCount();
    }

    private void UpdateCanReset()
    {
        if (SelectedImage == null)
        {
            CanReset = false;
            return;
        }

        // Note: Rotation is excluded - Reset only affects color/tonal adjustments
        var hasCurveEdits = CurrentCurve != null && !CurrentCurve.IsIdentity();
        CanReset = Exposure != 0.0 ||
                   !_liveWhiteBalance.IsIdentity ||
                   Brightness != 0 ||
                   Contrast != 0 ||
                   Saturation != 0 ||
                   Vibrance != 0 ||
                   Shadows != 0 ||
                   Highlights != 0 ||
                   // Keep hidden RAW-only state resettable after fallback.
                   HlReconstruction != HlReconstructionMode.Clip ||
                   hasCurveEdits ||
                   ActivePresetId != null;
    }

    private void ResetSliders()
    {
        Exposure = 0;
        ResetWhiteBalanceUi();
        ResetHighlightReconstructionUi();
        Brightness = 0;
        Contrast = 0;
        Saturation = 0;
        Vibrance = 0;
        Shadows = 0;
        Highlights = 0;
        Rotation = 0;
        HorizonRotation = 0.0;
        CurrentCurve = SelectedImage?.EditSettings.Curve;
        UpdateCanReset();
    }

    public async Task OnCurveChangedAsync()
    {
        if (SelectedImage == null || CurrentCurve == null) return;

        // Push undo state before making changes
        PushUndoState();

        // Rebuild the lookup table after curve changes
        CurrentCurve.BuildLookupTable();

        // Trigger live preview update and auto-save
        await UpdatePreviewWithCurrentSliders();
        await AutoSaveAsync();
        UpdateCanReset();
    }
}

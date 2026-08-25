using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnSelectedImageChanged(ImageFile? oldValue, ImageFile? newValue)
    {
        UpdateAdjacentWarmDirection(oldValue, newValue);
        CancelAdjacentPreviewWarm(invalidateWorker: true);
        var surfaceGeneration = ReserveRenderOutcome(
            PreviewSurfaceIntent.Edited,
            promotionEligible: true);
        if (oldValue != null && newValue != null)
        {
            ImageService.Previews.FlushRenderedPreviewCache();
            ImageService.Previews.InvalidatePreviewBase();
        }
        ClearCurveGesture();
        ActiveMixerBand = ColorMixerBand.Red;
        CancelRestingPreview(clearParent: true);
        ClearNavigatorVisibleRegion();
        OriginalViewPixelSize = default;

        // Update IsActive flags for visual highlighting in Browse grid
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null) newValue.IsActive = true;

        HasSelectedImage = newValue != null;
        ResetSelectedMetadataState(newValue);
        Volatile.Write(ref _activeBaseRefreshRequestId, 0);
        OnPropertyChanged(nameof(ActiveFileName));
        NotifySelectedImageEditStateChanged();
        NotifyFullScreenSelectionBadgeChanged();
        OnPreviewFailureSelectionChanged();
        ApplySelectionOutcome(newValue, surfaceGeneration);

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
            _lastAppliedEditSettings = null;
            SignalBackgroundActivityStarted();
            RefreshSourceAvailability(newValue);
            surfaceGeneration = LatestPreviewOutcomeGeneration;
            ResetSelectedMetadataState(newValue);
            RetryDeferredThumbnailIfAvailable(newValue);
            NotifyWhiteBalanceCommandState();
            ActiveCurveChannel = ToneCurveChannel.Composite;
            if (newValue.SourceRequiresHydration)
            {
                _histogramDebounce?.Cancel();
                ResetSliders();
                CurrentCrop = null;
                LoadCurrentCurveFrom(null);
                ActivePresetId = null;
                _lastSavedState = null;
                _isLoadingImage = false;
                UpdateCanReset();

                if (IsDevelopMode || IsFullScreenMode)
                {
                    if (!_suppressSelectionPreviewLoad)
                    {
                        _ = LoadPreviewAsync(
                            newValue,
                            surfaceGeneration);
                    }
                }
                else
                {
                    ScheduleHistogramUpdate();
                }
                return;
            }

            LoadSlidersFrom(newValue.EditSettings);
            _lastSavedState = newValue.EditSettings.Clone();

            _isLoadingImage = false;

            if (IsDevelopMode || IsFullScreenMode)
            {
                if (!_suppressSelectionPreviewLoad)
                {
                    _ = LoadPreviewAsync(
                        newValue,
                        surfaceGeneration);
                }
            }
            else
            {
                ScheduleHistogramUpdate();
            }

            // Load Metadata
            StartSelectionMetadataLoad(newValue);
            UpdateCanReset();
        }
        else
        {
            _lastAppliedEditSettings = null;
            // Clear cached preview when no image is selected
            ImageService.Previews.ClearPreviewCache();

            CurrentCrop = null;
            LoadCurrentCurveFrom(null);
            ActivePresetId = null;
            IsFullScreenMode = false;
            _lastSavedState = null;
            ResetSliders();
            _isLoadingImage = false;
        }
        NotifyClippingCommandState();
    }

    private void OnBrowseFilterChanged(object? sender, EventArgs e)
    {
        CancelAdjacentPreviewWarm(invalidateWorker: true);
        if (!Browse.ContainsVisible(SelectedImage))
        {
            SelectedImage = Browse.FirstVisible();
        }
        else if (SelectedImage == null)
        {
            SelectedImage = Browse.FirstVisible();
        }

        UpdateSelectedCount();
    }

    private ActionTargetResolution ResolveActionTargets()
    {
        if (IsFullScreenMode)
        {
            return new ActionTargetResolution([], false);
        }

        if (!IsDevelopMode)
        {
            var selected = Browse.GetSelectedImages().ToList();
            if (selected.Count > 0)
            {
                return new ActionTargetResolution(
                    selected.Where(image =>
                        !IsDeleteTargetClaimed(image.FilePath)).ToList(),
                    true);
            }
        }

        IReadOnlyList<ImageFile> targets = SelectedImage == null ||
            IsDeleteTargetClaimed(SelectedImage.FilePath)
            ? []
            : [SelectedImage];
        return new ActionTargetResolution(targets, false);
    }

    private readonly record struct ActionTargetResolution(
        IReadOnlyList<ImageFile> Targets,
        bool IsBrowseSelection);

    private void UpdateCanReset()
    {
        if (!CanEditSelectedImage)
        {
            CanReset = false;
            return;
        }

        // Note: Rotation is excluded - Reset only affects color/tonal adjustments
        var hasCurveEdits = SelectedImage?.EditSettings.Curve is { } composite &&
                !composite.IsIdentity() ||
            SelectedImage?.EditSettings.CurveRed is { } red && !red.IsIdentity() ||
            SelectedImage?.EditSettings.CurveGreen is { } green && !green.IsIdentity() ||
            SelectedImage?.EditSettings.CurveBlue is { } blue && !blue.IsIdentity();
        CanReset = Exposure != 0.0 ||
                   !_liveWhiteBalance.IsIdentity ||
                   Brightness != 0 ||
                   Contrast != 0 ||
                   Saturation != 0 ||
                   Vibrance != 0 ||
                   Shadows != 0 ||
                   Highlights != 0 ||
                   // Keep disabled RAW-only state resettable after fallback.
                   HlReconstruction != HlReconstructionMode.Clip ||
                   CaptureSharpen != CaptureSharpenDefault ||
                   LuminanceNr != 0 ||
                   ChromaNr != 0 ||
                   Vignette != 0 ||
                   Grain != 0 ||
                   _liveMixer.HasActivePixels ||
                   LensDistortion != SelectedImage!.EditSettings.Lens.BaselineDistortion ||
                   LensChromaticAberration != SelectedImage.EditSettings.Lens.BaselineChromaticAberration ||
                   LensVignetting != SelectedImage.EditSettings.Lens.BaselineVignetting ||
                   GeometryVertical != 0 ||
                   GeometryHorizontal != 0 ||
                   GeometryAspect != 0 ||
                   GeometryDistortion != 0 ||
                   hasCurveEdits ||
                   ActivePresetId != null ||
                   SelectedImage?.EditSettings.RawProfile != null;
    }

    private void ResetSliders()
    {
        Exposure = 0;
        ResetWhiteBalanceUi();
        ResetHighlightReconstructionUi();
        ResetDetailUi();
        ResetEffectsUi();
        ResetMixerUi();
        ResetLensUi();
        ResetGeometryUi();
        Brightness = 0;
        Contrast = 0;
        Saturation = 0;
        Vibrance = 0;
        Shadows = 0;
        Highlights = 0;
        Rotation = 0;
        HorizonRotation = 0.0;
        LoadCurrentCurveFrom(SelectedImage?.EditSettings);
        UpdateCanReset();
    }
}

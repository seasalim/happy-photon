using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    partial void OnSelectedImageChanged(ImageFile? oldValue, ImageFile? newValue)
    {
        ClearCurveGesture();
        CancelRestingPreview(clearParent: true);
        ClearNavigatorVisibleRegion();
        OriginalViewPixelSize = default;

        // Update IsActive flags for visual highlighting in Library grid
        if (oldValue != null) oldValue.IsActive = false;
        if (newValue != null) newValue.IsActive = true;

        HasSelectedImage = newValue != null;
        IsBrightnessEnabled = newValue?.IsRaw != true;
        ResetSelectedMetadataState(newValue);
        IsShowingOriginal = false;
        ClearPreviewClippingArtifacts();
        Volatile.Write(ref _activeBaseRefreshRequestId, 0);
        IsBaseArming = false;
        OnPropertyChanged(nameof(ActiveFileName));
        NotifySelectedImageEditStateChanged();
        NotifyFullScreenSelectionBadgeChanged();
        OnPreviewFailureSelectionChanged();

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
            SignalBackgroundActivityStarted();
            RefreshSourceAvailability(newValue);
            ResetSelectedMetadataState(newValue);
            RetryDeferredThumbnailIfAvailable(newValue);
            PrepareWhiteBalanceUi(newValue);
            NotifyWhiteBalanceCommandState();
            ActiveCurveChannel = ToneCurveChannel.Composite;
            if (newValue.SourceRequiresHydration)
            {
                _histogramDebounce?.Cancel();
                Histogram = null;
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
                        _ = LoadPreviewAsync(newValue, wakeActivity: false);
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
                    _ = LoadPreviewAsync(newValue, wakeActivity: false);
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
            // Clear cached preview when no image is selected
            ImageService.ClearPreviewCache();

            ClearPreviewImage();
            Histogram = null;
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

    private ActionTargetResolution ResolveActionTargets()
    {
        if (IsFullScreenMode)
        {
            return new ActionTargetResolution([], false);
        }

        if (!IsDevelopMode)
        {
            var selected = Library.GetSelectedImages().ToList();
            if (selected.Count > 0)
            {
                return new ActionTargetResolution(selected, true);
            }
        }

        IReadOnlyList<ImageFile> targets = SelectedImage == null
            ? []
            : [SelectedImage];
        return new ActionTargetResolution(targets, false);
    }

    private readonly record struct ActionTargetResolution(
        IReadOnlyList<ImageFile> Targets,
        bool IsLibrarySelection);

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
                   NoiseReduction != FbddMode.Off ||
                   ChromaNr != 0 ||
                   hasCurveEdits ||
                   ActivePresetId != null;
    }

    private void ResetSliders()
    {
        Exposure = 0;
        ResetWhiteBalanceUi();
        ResetHighlightReconstructionUi();
        ResetDetailUi();
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

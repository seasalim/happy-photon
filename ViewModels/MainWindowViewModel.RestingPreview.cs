using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly TimeSpan RestingSettleDelay =
        TimeSpan.FromMilliseconds(75);
    private const int RestingBoundGrowthTolerance = 4;

    private CancellationTokenSource? _restingRenderCts;
    private CancellationTokenRegistration _restingEditCancellationRegistration;
    private PreviewRenderIdentity? _restingParent;
    private EditSettings? _restingSettings;
    private int _requiredDeviceLongEdge;
    private int _restingHighestAttemptedBound;
    private int _restingAchievableLongEdge;
    private int _restingSatisfiedLongEdge;
    private long _restingScheduleSerial;
    private long _restingSurfaceGeneration;
    private int _restingPaintCount;

    internal int RequiredDeviceLongEdge => _requiredDeviceLongEdge;
    internal int RestingPaintCount => Volatile.Read(ref _restingPaintCount);
    internal bool HasArmedRestingRender =>
        Volatile.Read(ref _restingRenderCts) != null;

    internal void PublishRequiredDeviceLongEdge(int longEdge)
    {
        var normalized = Math.Max(0, longEdge);
        if (_requiredDeviceLongEdge == normalized)
        {
            return;
        }

        var previous = _requiredDeviceLongEdge;
        var grew = normalized > previous;
        _requiredDeviceLongEdge = normalized;
        ImageServiceHelpers.LogDisplayTrace(
            $"resting bound={normalized} prev={previous} grew={grew} " +
            $"parent={(_restingParent == null ? "null" : _restingParent.Generation.ToString())} " +
            $"satisfied={_restingSatisfiedLongEdge} " +
            $"attempted={_restingHighestAttemptedBound} " +
            $"achievable={_restingAchievableLongEdge}");
        if (grew && _restingParent != null)
        {
            ScheduleRestingRender();
        }
        else if ((long)normalized + RestingBoundGrowthTolerance < previous)
        {
            _restingHighestAttemptedBound = Math.Min(
                _restingHighestAttemptedBound,
                normalized);
            CancelRestingTimerOnly();
        }
    }

    private void OnAcceptedInteractivePreview(Bitmap bitmap)
    {
        var identity = ImageService.Previews.TryGetPreviewRenderIdentity(bitmap);
        if (identity == null ||
            !ReferenceEquals(identity.ImageFile, SelectedImage) ||
            SelectedImage == null)
        {
            CancelRestingPreview(clearParent: true);
            return;
        }

        var settings = CaptureRestingSettings();
        if (!string.Equals(
                identity.SettingsHash,
                RenderSettingsHash.Compute(settings),
                StringComparison.Ordinal))
        {
            CancelRestingPreview(clearParent: true);
            return;
        }

        var parentChanged = _restingParent == null ||
            _restingParent.Generation != identity.Generation ||
            !string.Equals(
                _restingParent.DecodeKey,
                identity.DecodeKey,
                StringComparison.Ordinal);
        _restingParent = identity;
        _restingSurfaceGeneration = Volatile.Read(
            ref _latestPreviewOutcomeGeneration);
        _restingSettings = settings;
        if (parentChanged)
        {
            _restingHighestAttemptedBound = 0;
            _restingAchievableLongEdge = 0;
            _restingSatisfiedLongEdge = Math.Max(
                bitmap.PixelSize.Width,
                bitmap.PixelSize.Height);
        }
        _restingEditCancellationRegistration.Dispose();
        var editDebounce = ActiveEditDebounce();
        _restingEditCancellationRegistration = editDebounce.Token.Register(
            () => InvalidateRestingParent(identity));
        ScheduleRestingRender();
    }

    // Never hand out a cancelled edit-debounce source: registering on a
    // cancelled token fires synchronously and would invalidate the parent the
    // instant it is set (the permanently-unarmed mode-round-trip defect).
    private CancellationTokenSource ActiveEditDebounce()
    {
        var current = _previewDebounce;
        if (current == null || current.IsCancellationRequested)
        {
            current?.Dispose();
            current = new CancellationTokenSource();
            _previewDebounce = current;
        }
        return current;
    }

    // Mirrors the interactive temp-settings construction exactly (Clone
    // carries the curve set) so the resting settings hash always matches an
    // accepted interactive paint.
    private EditSettings CaptureRestingSettings()
    {
        var settings = SelectedImage!.EditSettings.Clone();
        SaveSlidersTo(settings);
        settings.Rotation = Rotation;
        settings.HorizonRotation = HorizonRotation;
        settings.Crop = CurrentCrop?.Clone();
        return settings;
    }

    private void ScheduleRestingRender()
    {
        var declined =
            _restingParent == null ? "no-parent"
            : _restingSettings == null ? "no-settings"
            : SelectedImage == null ? "no-image"
            : !IsDevelopMode && !IsFullScreenMode ? "no-surface"
            : IsCropMode ? "crop"
            : IsShowingOriginal ? "original"
            : _isHoveringPreset ? "preset-hover"
            : !ExceedsRestingBound(_restingSatisfiedLongEdge) ? "satisfied"
            : !ExceedsRestingBound(_restingHighestAttemptedBound) ? "attempted"
            : _restingAchievableLongEdge > 0 &&
              _restingSatisfiedLongEdge >= _restingAchievableLongEdge
                ? "achievable-ceiling"
                : null;
        if (declined != null)
        {
            ImageServiceHelpers.LogDisplayTrace(
                $"resting declined={declined} bound={_requiredDeviceLongEdge}");
            return;
        }
        ImageServiceHelpers.LogDisplayTrace(
            $"resting armed bound={_requiredDeviceLongEdge}");

        CancelRestingTimerOnly();
        var editToken = ActiveEditDebounce().Token;
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            editToken);
        _restingRenderCts = cancellation;
        var serial = Interlocked.Increment(ref _restingScheduleSerial);
        _ = DebouncedAction.RunAsync(
            "resting preview render",
            RestingSettleDelay,
            cancellation.Token,
            () => RenderRestingPreviewAsync(serial, cancellation),
            timeProvider: _timeProvider);
    }

    private bool ExceedsRestingBound(int bound) =>
        (long)_requiredDeviceLongEdge >
        (long)bound + RestingBoundGrowthTolerance;

    private async Task RenderRestingPreviewAsync(
        long serial,
        CancellationTokenSource cancellation)
    {
        var image = SelectedImage;
        var parent = _restingParent;
        var settings = _restingSettings?.Clone();
        var requested = _requiredDeviceLongEdge;
        var surfaceGeneration = _restingSurfaceGeneration;
        if (image == null || parent == null || settings == null)
        {
            return;
        }

        _restingHighestAttemptedBound = Math.Max(
            _restingHighestAttemptedBound,
            requested);
        using var result = await ImageService.Previews.RenderRestingPreviewAsync(
            image,
            settings,
            requested,
            parent,
            cancellation.Token);
        if (result == null)
        {
            ImageServiceHelpers.LogDisplayTrace(
                $"resting dropped=service-null requested={requested}");
            return;
        }
        var dropped =
            cancellation.IsCancellationRequested ? "cancelled"
            : serial != Volatile.Read(ref _restingScheduleSerial) ? "serial"
            : !ReferenceEquals(_restingRenderCts, cancellation) ? "cts"
            : !ReferenceEquals(SelectedImage, image) ? "image"
            : _restingParent?.Generation != parent.Generation ? "generation"
            : surfaceGeneration != Volatile.Read(
                ref _latestPreviewOutcomeGeneration) ? "surface-generation"
            : !IsDevelopMode && !IsFullScreenMode ? "surface"
            : IsCropMode || IsShowingOriginal || _isHoveringPreset
                ? "transient"
                : null;
        if (dropped != null)
        {
            ImageServiceHelpers.LogDisplayTrace(
                $"resting dropped={dropped} requested={requested}");
            return;
        }

        _restingAchievableLongEdge = result.AchievableLongEdge;
        _restingSatisfiedLongEdge = Math.Max(
            _restingSatisfiedLongEdge,
            result.RenderedLongEdge);
        if (PreviewImage != null &&
            result.RenderedLongEdge <= Math.Max(
                PreviewImage.PixelSize.Width,
                PreviewImage.PixelSize.Height))
        {
            return;
        }

        ApplyRenderOutcome(RenderOutcome.Resting(
            image,
            surfaceGeneration,
            result.DetachBitmap()));
    }

    private void ReplaceWithRestingPreview(Bitmap preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (ReferenceEquals(PreviewImage, preview))
        {
            return;
        }

        if (ImageServiceHelpers.DisplayTraceLoggingEnabled)
        {
            var restingIdentity =
                ImageService.Previews.TryGetPreviewRenderIdentity(preview);
            ImageServiceHelpers.LogDisplayTrace(
                $"paint source={PaintSourceLabel(PreviewPaintSource.RestingRender)} " +
                $"bitmap={preview.PixelSize.Width}x{preview.PixelSize.Height} " +
                $"luma={BitmapConversionService.EstimateMeanLuma(preview):F4} " +
                $"decode={restingIdentity?.DecodeKey ?? "none"} " +
                $"settings={restingIdentity?.SettingsHash ?? "none"}");
        }
        var previous = PreviewImage;
        var transferred = false;
        if (previous != null)
        {
            var identity = ImageService.Previews.TryGetPreviewRenderIdentity(previous);
            transferred = identity != null &&
                ImageService.Previews.TransferCurrentRenderedBitmap(
                    previous, identity);
        }
        PreviewImage = preview;
        if (previous != null && !transferred)
        {
            _bitmapRetirement.Retire(
                previous,
                () => ReferenceEquals(PreviewImage, previous));
        }
        Interlocked.Increment(ref _restingPaintCount);
    }

    private void InvalidateRestingParent(PreviewRenderIdentity identity)
    {
        if (_restingParent?.Generation != identity.Generation ||
            !string.Equals(
                _restingParent.DecodeKey,
                identity.DecodeKey,
                StringComparison.Ordinal))
        {
            return;
        }

        Interlocked.Increment(ref _restingScheduleSerial);
        CancelRestingTimerOnly();
        ClearRestingParentState();
    }

    private void CancelRestingPreview(bool clearParent = false)
    {
        Interlocked.Increment(ref _restingScheduleSerial);
        CancelRestingTimerOnly();
        if (!clearParent)
        {
            return;
        }

        _restingEditCancellationRegistration.Dispose();
        _restingEditCancellationRegistration = default;
        ClearRestingParentState();
    }

    private void ClearRestingParentState()
    {
        _restingParent = null;
        _restingSurfaceGeneration = 0;
        _restingSettings = null;
        _restingHighestAttemptedBound = 0;
        _restingAchievableLongEdge = 0;
        _restingSatisfiedLongEdge = 0;
    }

    private void CancelRestingTimerOnly()
    {
        var cancellation = Interlocked.Exchange(
            ref _restingRenderCts,
            null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }
}

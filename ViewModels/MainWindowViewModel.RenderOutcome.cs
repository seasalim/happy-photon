using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

internal enum PreviewSurfaceIntent
{
    Edited,
    Original
}

internal enum RenderOutcomeClass
{
    Selection,
    StateDefining,
    CachedUpgrade,
    RestingUpgrade,
    ClippingUpgrade,
    Failure,
    Rollback,
    Availability
}

internal enum OutcomeFieldMode
{
    Preserve,
    Set,
    Clear
}

internal sealed partial class RenderOutcome : IDisposable
{
    private Bitmap? _bitmap;
    private ClippingMask? _clippingMask;
    private PreviewPromotionLease? _promotionLease;

    public required ImageFile? Image { get; init; }
    public required long Generation { get; init; }
    public required RenderOutcomeClass Class { get; init; }
    public required PreviewSurfaceIntent Intent { get; set; }
    public PreviewPaintSource PaintSource { get; init; }
    public bool Succeeded { get; init; } = true;
    public BaseImageLoadFailure Failure { get; init; }
    public bool IsBaseStale { get; init; }
    public bool Promotable { get; init; }
    public OutcomeFieldMode BitmapMode { get; init; }
    public OutcomeFieldMode HistogramMode { get; init; }
    public HistogramData? Histogram { get; init; }
    public OutcomeFieldMode ClippingMode { get; init; }
    public ClippingStats? Clipping { get; init; }
    public OutcomeFieldMode CapabilityMode { get; init; }
    public bool IsRawSource { get; init; }
    public bool IsMonochrome { get; init; }
    public OutcomeFieldMode ProfileMode { get; init; }
    public DcpProfileState? ProfileState { get; init; }
    public OutcomeFieldMode WhiteBalanceMode { get; init; }
    public double AsShotKelvin { get; init; }
    public double AsShotTint { get; init; }
    public OutcomeFieldMode RawHistogramMode { get; init; }
    public HistogramData? RawHistogram { get; init; }
    public OutcomeFieldMode LensMode { get; init; }
    public LensPrescriptionSummary? LensPrescription { get; init; }
    public PreviewSurfaceIntent? RollbackRequestedIntent { get; init; }

    public static RenderOutcome FromArtifacts(
        ImageFile image,
        long generation,
        PreviewSurfaceIntent intent,
        RenderOutcomeClass outcomeClass,
        PreviewPaintSource source,
        PreviewArtifacts artifacts,
        bool promotable,
        PreviewSurfaceIntent? rollbackRequestedIntent = null)
    {
        var bitmap = artifacts.DetachBitmap();
        var stale = artifacts.IsBaseStale;
        var hasPaint = bitmap != null;
        return new RenderOutcome
        {
            Image = image,
            Generation = generation,
            Class = outcomeClass,
            Intent = intent,
            PaintSource = source,
            Succeeded = bitmap != null,
            IsBaseStale = stale,
            Promotable = promotable && !stale,
            RollbackRequestedIntent = rollbackRequestedIntent,
            BitmapMode = bitmap == null
                ? OutcomeFieldMode.Preserve
                : OutcomeFieldMode.Set,
            _bitmap = bitmap,
            HistogramMode = bitmap == null
                ? OutcomeFieldMode.Preserve
                : OutcomeFieldMode.Set,
            Histogram = artifacts.Histogram,
            ClippingMode = !hasPaint
                ? OutcomeFieldMode.Preserve
                : stale
                ? OutcomeFieldMode.Clear
                : OutcomeFieldMode.Set,
            Clipping = stale ? null : artifacts.Clipping,
            _clippingMask = stale ? null : artifacts.DetachClippingMask(),
            CapabilityMode = !hasPaint || stale
                ? OutcomeFieldMode.Preserve
                : OutcomeFieldMode.Set,
            IsRawSource = artifacts.IsRawSource,
            IsMonochrome = artifacts.IsMonochrome,
            ProfileMode = !hasPaint || stale
                ? OutcomeFieldMode.Preserve
                : OutcomeFieldMode.Set,
            ProfileState = stale ? null : artifacts.ProfileState,
            WhiteBalanceMode = !hasPaint || stale
                ? OutcomeFieldMode.Preserve
                : OutcomeFieldMode.Set,
            AsShotKelvin = artifacts.AsShotKelvin,
            AsShotTint = artifacts.AsShotTint,
            RawHistogramMode = !hasPaint || stale
                ? OutcomeFieldMode.Preserve
                : artifacts.RawHistogram == null
                    ? OutcomeFieldMode.Clear
                    : OutcomeFieldMode.Set,
            RawHistogram = stale ? null : artifacts.RawHistogram,
            LensMode = !hasPaint || stale
                ? OutcomeFieldMode.Preserve
                : OutcomeFieldMode.Set,
            LensPrescription = stale ? null : artifacts.LensPrescription,
            _promotionLease = artifacts.DetachPromotionLease()
        };
    }

    public static RenderOutcome FromRefresh(
        PreviewRefresh refresh,
        Bitmap bitmap,
        ClippingMask? clippingMask,
        PreviewPromotionLease? promotionLease,
        PreviewSurfaceIntent intent) => new()
    {
        Image = refresh.ImageFile,
        Generation = refresh.Generation,
        Class = RenderOutcomeClass.StateDefining,
        Intent = intent,
        PaintSource = PreviewPaintSource.BackgroundRefresh,
        BitmapMode = OutcomeFieldMode.Set,
        _bitmap = bitmap,
        HistogramMode = refresh.HasHistogram
            ? OutcomeFieldMode.Set
            : OutcomeFieldMode.Preserve,
        Histogram = refresh.Histogram,
        ClippingMode = OutcomeFieldMode.Set,
        Clipping = refresh.Clipping,
        _clippingMask = clippingMask,
        CapabilityMode = OutcomeFieldMode.Set,
        IsRawSource = refresh.IsRawSource,
        IsMonochrome = refresh.IsMonochrome,
        ProfileMode = OutcomeFieldMode.Set,
        ProfileState = refresh.ProfileState,
        WhiteBalanceMode = OutcomeFieldMode.Set,
        AsShotKelvin = refresh.AsShotKelvin,
        AsShotTint = refresh.AsShotTint,
        RawHistogramMode = refresh.RawHistogram == null
            ? OutcomeFieldMode.Clear
            : OutcomeFieldMode.Set,
        RawHistogram = refresh.RawHistogram,
        LensMode = OutcomeFieldMode.Set,
        LensPrescription = refresh.LensPrescription,
        Promotable = true,
        _promotionLease = promotionLease
    };

    public Bitmap? DetachBitmap() => Interlocked.Exchange(ref _bitmap, null);

    public ClippingMask? DetachClippingMask() =>
        Interlocked.Exchange(ref _clippingMask, null);

    public void CommitPromotion(Bitmap bitmap)
    {
        Interlocked.Exchange(ref _promotionLease, null)?.Commit(bitmap);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _promotionLease, null)?.Dispose();
        Interlocked.Exchange(ref _clippingMask, null)?.Dispose();
        Interlocked.Exchange(ref _bitmap, null)?.Dispose();
    }
}

public partial class MainWindowViewModel
{
    private long _latestPreviewOutcomeGeneration;
    private bool _renderOutcomeChannelClosed;
    private bool _stateDefiningPaintApplied;
    private bool _currentBasePaintApplied;
    private bool _currentGenerationPromotionEligible = true;
    private PreviewSurfaceIntent _requestedPreviewIntent;
    private PreviewSurfaceIntent _appliedPreviewIntent;
    private EditSettings? _lastAppliedEditSettings;

    internal long LatestPreviewOutcomeGeneration =>
        Volatile.Read(ref _latestPreviewOutcomeGeneration);

    private long ReserveRenderOutcome(
        PreviewSurfaceIntent? requestedIntent = null,
        bool? promotionEligible = null)
    {
        if (_renderOutcomeChannelClosed)
        {
            return Volatile.Read(ref _latestPreviewOutcomeGeneration);
        }

        if (requestedIntent.HasValue)
        {
            _requestedPreviewIntent = requestedIntent.Value;
        }
        if (promotionEligible.HasValue)
        {
            _currentGenerationPromotionEligible = promotionEligible.Value;
        }
        _stateDefiningPaintApplied = false;
        _currentBasePaintApplied = false;
        return Interlocked.Increment(ref _latestPreviewOutcomeGeneration);
    }

    private long RequestEditedRender() =>
        ReserveRenderOutcome(
            PreviewSurfaceIntent.Edited,
            promotionEligible: true);

    private void ApplySelectionOutcome(ImageFile? image, long generation)
    {
        var isRaw = image?.IsRaw == true;
        ApplyRenderOutcome(new RenderOutcome
        {
            Image = image,
            Generation = generation,
            Class = RenderOutcomeClass.Selection,
            Intent = PreviewSurfaceIntent.Edited,
            BitmapMode = OutcomeFieldMode.Clear,
            HistogramMode = OutcomeFieldMode.Clear,
            ClippingMode = OutcomeFieldMode.Clear,
            CapabilityMode = OutcomeFieldMode.Set,
            IsRawSource = isRaw,
            IsMonochrome = false,
            ProfileMode = OutcomeFieldMode.Set,
            WhiteBalanceMode = OutcomeFieldMode.Set,
            AsShotKelvin = isRaw ? 5500 : 6504,
            RawHistogramMode = OutcomeFieldMode.Clear,
            LensMode = OutcomeFieldMode.Set
        });
    }

    private void ApplySurfaceClearOutcome(ImageFile? image, long generation)
    {
        ApplyRenderOutcome(new RenderOutcome
        {
            Image = image,
            Generation = generation,
            Class = RenderOutcomeClass.Availability,
            Intent = _requestedPreviewIntent,
            BitmapMode = OutcomeFieldMode.Clear,
            HistogramMode = OutcomeFieldMode.Clear,
            ClippingMode = OutcomeFieldMode.Clear,
            RawHistogramMode = OutcomeFieldMode.Clear
        });
    }

    private bool ApplyRenderOutcome(RenderOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        using (outcome)
        {
            if (_renderOutcomeChannelClosed ||
                outcome.Generation != Volatile.Read(
                    ref _latestPreviewOutcomeGeneration) ||
                !ReferenceEquals(SelectedImage, outcome.Image))
            {
                return false;
            }

            if ((outcome.Class == RenderOutcomeClass.CachedUpgrade &&
                 (_stateDefiningPaintApplied ||
                  outcome.Intent != _requestedPreviewIntent)) ||
                (outcome.Class == RenderOutcomeClass.StateDefining &&
                 outcome.Intent != _requestedPreviewIntent) ||
                (outcome.Class == RenderOutcomeClass.RestingUpgrade &&
                 (!_stateDefiningPaintApplied ||
                  _requestedPreviewIntent != PreviewSurfaceIntent.Edited)))
            {
                return false;
            }

            if (outcome.Class == RenderOutcomeClass.StateDefining &&
                outcome.IsBaseStale && _currentBasePaintApplied)
            {
                // This generation's fresh-base paint already landed, so keep
                // its pixels and facts — but a successful stale render still
                // counts as applied, or the edit that requested it would skip
                // its autosave and be lost.
                return outcome.Succeeded;
            }

            if (outcome.Class == RenderOutcomeClass.Rollback ||
                outcome.Class == RenderOutcomeClass.StateDefining &&
                !outcome.Succeeded)
            {
                _requestedPreviewIntent = outcome.RollbackRequestedIntent ??
                    _appliedPreviewIntent;
            }

            ApplyOutcomeFacts(outcome);
            var painted = ApplyOutcomeBitmap(outcome);
            if (outcome.Class == RenderOutcomeClass.StateDefining && painted)
            {
                _stateDefiningPaintApplied = true;
                _currentBasePaintApplied |= !outcome.IsBaseStale;
                _appliedPreviewIntent = outcome.Intent;
                IsShowingOriginal =
                    _appliedPreviewIntent == PreviewSurfaceIntent.Original;
            }
            else if (outcome.Class is RenderOutcomeClass.Selection or
                     RenderOutcomeClass.Availability)
            {
                _appliedPreviewIntent = PreviewSurfaceIntent.Edited;
                IsShowingOriginal = false;
            }

            if (painted && outcome.Promotable &&
                _currentGenerationPromotionEligible &&
                outcome.Intent == PreviewSurfaceIntent.Edited &&
                !IsCropMode && !_isHoveringPreset)
            {
                outcome.CommitPromotion(PreviewImage!);
                OnAcceptedInteractivePreview(PreviewImage!);
            }

            return painted || outcome.Class is not
                (RenderOutcomeClass.CachedUpgrade or
                 RenderOutcomeClass.RestingUpgrade);
        }
    }

    private void ApplyOutcomeFacts(RenderOutcome outcome)
    {
        if (outcome.HistogramMode != OutcomeFieldMode.Preserve)
        {
            var nextHistogram = outcome.HistogramMode == OutcomeFieldMode.Clear
                ? null
                : outcome.Histogram;
            if (nextHistogram != null && nextHistogram.Waveform == null &&
                Histogram?.Waveform is { } priorWaveform)
            {
                // Histogram-active ticks skip waveform accumulation. Retain the
                // prior trace so switching scopes is immediate; selecting the
                // waveform schedules a coherent refresh from the current image.
                nextHistogram.Waveform = priorWaveform;
            }
            Histogram = nextHistogram;
        }
        if (outcome.ClippingMode != OutcomeFieldMode.Preserve)
        {
            var mask = outcome.ClippingMode == OutcomeFieldMode.Clear
                ? null
                : outcome.DetachClippingMask();
            var requestedSides = RequestedClippingOverlaySides;
            var preserveMask = outcome.ClippingMode != OutcomeFieldMode.Clear &&
                requestedSides != ClippingOverlaySide.None &&
                (mask == null ||
                 (mask.Sides & requestedSides) != requestedSides);
            if (requestedSides == ClippingOverlaySide.None)
            {
                mask?.Dispose();
                mask = null;
            }
            ApplyPreviewClipping(
                outcome.ClippingMode == OutcomeFieldMode.Clear
                    ? null
                    : outcome.Clipping,
                mask,
                preserveMask);
        }
        if (outcome.CapabilityMode == OutcomeFieldMode.Set &&
            outcome.Image != null)
        {
            ReconcileHighlightReconstructionCapability(
                outcome.Image,
                outcome.IsRawSource);
            ReconcileMonochromeCapability(
                outcome.Image,
                outcome.IsMonochrome);
        }
        if (outcome.ProfileMode == OutcomeFieldMode.Set)
        {
            if (outcome.Class == RenderOutcomeClass.Selection)
            {
                ResetRawProfilePicker(outcome.Image);
            }
            else if (outcome.Image != null)
            {
                ApplyRawProfileState(
                    outcome.Image,
                    outcome.IsRawSource,
                    outcome.ProfileState);
            }
        }
        if (outcome.WhiteBalanceMode == OutcomeFieldMode.Set)
        {
            ApplyWhiteBalanceContext(
                outcome.AsShotKelvin,
                outcome.AsShotTint,
                outcome.Class != RenderOutcomeClass.Selection);
        }
        if (outcome.RawHistogramMode != OutcomeFieldMode.Preserve)
        {
            SetRawHistogram(
                outcome.RawHistogramMode == OutcomeFieldMode.Clear
                    ? null
                    : outcome.RawHistogram);
        }
        if (outcome.LensMode == OutcomeFieldMode.Set)
        {
            ApplyLensPrescription(outcome.IsRawSource, outcome.LensPrescription);
        }
        ApplyPreviewFailure(outcome);
    }

    private bool ApplyOutcomeBitmap(RenderOutcome outcome)
    {
        if (outcome.BitmapMode == OutcomeFieldMode.Preserve)
        {
            return false;
        }
        if (outcome.BitmapMode == OutcomeFieldMode.Clear)
        {
            ClearPreviewImage();
            return false;
        }
        if (!IsDevelopMode && !IsFullScreenMode)
        {
            return false;
        }

        var bitmap = outcome.DetachBitmap();
        if (bitmap == null)
        {
            return false;
        }
        if (outcome.Class == RenderOutcomeClass.RestingUpgrade)
        {
            ReplaceWithRestingPreview(bitmap);
        }
        else
        {
            ReplacePreviewImage(bitmap, outcome.PaintSource);
        }
        return true;
    }

    private void CloseRenderOutcomeChannel()
    {
        _renderOutcomeChannelClosed = true;
        Interlocked.Increment(ref _latestPreviewOutcomeGeneration);
    }

}

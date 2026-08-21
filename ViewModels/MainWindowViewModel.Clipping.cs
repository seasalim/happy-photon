using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _clippingOverlayCts;
    private ClippingMask? _previewClippingMask;
    private ClippingOverlaySide _peekClippingSide;
    private long _clippingMaskSerial;

    public ClippingStats? DisplayClippingStats { get; private set; }
    public bool IsClippingStatsAvailable => DisplayClippingStats != null;

    public ClippingMask? PreviewClippingMask
    {
        get => _previewClippingMask;
        private set
        {
            if (ReferenceEquals(_previewClippingMask, value))
            {
                return;
            }

            var previous = _previewClippingMask;
            _previewClippingMask = value;
            OnPropertyChanged();
            previous?.Dispose();
        }
    }

    public bool IsClippingOverlayLatched { get; private set; }

    public ClippingOverlaySide VisibleClippingOverlaySides =>
        _peekClippingSide != ClippingOverlaySide.None
            ? _peekClippingSide
            : IsClippingOverlayLatched
                ? ClippingOverlaySide.Both
                : ClippingOverlaySide.None;

    internal ClippingOverlaySide RequestedClippingOverlaySides
    {
        get
        {
            if (!IsDevelopMode || IsFullScreenMode)
            {
                return ClippingOverlaySide.None;
            }
            if (IsClippingOverlayLatched)
            {
                return ClippingOverlaySide.Both;
            }
            return _peekClippingSide;
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggleClippingOverlay))]
    private void ToggleClippingOverlay()
    {
        if (!CanToggleClippingOverlay())
        {
            return;
        }

        IsClippingOverlayLatched = !IsClippingOverlayLatched;
        OnPropertyChanged(nameof(IsClippingOverlayLatched));
        OnPropertyChanged(nameof(VisibleClippingOverlaySides));
        if (SelectedImage is { } image)
        {
            ShowAssessmentFeedback(
                image,
                IsClippingOverlayLatched
                    ? "Clipping indicators on"
                    : "Clipping indicators off");
        }

        if (IsClippingOverlayLatched ||
            _peekClippingSide != ClippingOverlaySide.None)
        {
            RequestClippingOverlayRender();
        }
        else
        {
            RejectPendingClippingMasks();
            Interlocked.Increment(ref _clippingMaskSerial);
            PreviewClippingMask = null;
        }
    }

    private bool CanToggleClippingOverlay() =>
        IsWorkspaceInteractionEnabled &&
        IsDevelopMode &&
        !IsFullScreenMode &&
        CanEditSelectedImage;

    internal void BeginClippingPeek(ClippingOverlaySide side)
    {
        if (!CanToggleClippingOverlay() ||
            side is not (ClippingOverlaySide.Highlights or
                ClippingOverlaySide.DisplayFloor))
        {
            return;
        }

        _peekClippingSide = side;
        OnPropertyChanged(nameof(VisibleClippingOverlaySides));
        if (!IsClippingOverlayLatched)
        {
            RequestClippingOverlayRender();
        }
    }

    internal void EndClippingPeek()
    {
        if (_peekClippingSide == ClippingOverlaySide.None)
        {
            return;
        }

        _peekClippingSide = ClippingOverlaySide.None;
        OnPropertyChanged(nameof(VisibleClippingOverlaySides));
        if (!IsClippingOverlayLatched)
        {
            RejectPendingClippingMasks();
            Interlocked.Increment(ref _clippingMaskSerial);
            PreviewClippingMask = null;
        }
    }

    private void RequestClippingOverlayRender()
    {
        var debounce = ReplaceDebounce(ref _clippingOverlayCts);
        var serial = Interlocked.Increment(ref _clippingMaskSerial);
        _ = RenderClippingOverlayAsync(serial, debounce.Token);
    }

    private async Task RenderClippingOverlayAsync(
        long serial,
        CancellationToken cancellationToken)
    {
        try
        {
            var image = SelectedImage;
            if (image == null || !CanEditSelectedImage)
            {
                return;
            }
            var generation = Volatile.Read(ref _latestPreviewOutcomeGeneration);
            var intent = _requestedPreviewIntent;
            var settings = image.EditSettings.Clone();
            SaveSlidersTo(settings);
            settings.Rotation = Rotation;
            settings.HorizonRotation = HorizonRotation;
            settings.Crop = PreviewCrop();
            using var artifacts = await ImageService.Previews
                .ApplyEditsToPreviewArtifactsAsync(
                    image,
                    settings,
                    LibraryThumbnailRequest,
                    skipHistogram: true,
                    RequestedClippingOverlaySides,
                    cancellationToken,
                    generation);
            cancellationToken.ThrowIfCancellationRequested();
            if (serial != Volatile.Read(ref _clippingMaskSerial))
            {
                return;
            }

            // Mask rendering deliberately shares the current surface generation,
            // but publishes only clipping artifacts. It cannot repaint, advance
            // intent, arm resting, or commit a second promotion for that generation.
            ApplyRenderOutcome(RenderOutcome.FromClippingArtifacts(
                image,
                generation,
                intent,
                artifacts));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RejectPendingClippingMasks()
    {
        _clippingOverlayCts?.Cancel();
    }

    private void ApplyPreviewClipping(
        ClippingStats? clipping,
        ClippingMask? clippingMask,
        bool preserveMask = false)
    {
        DisplayClippingStats = clipping;
        if (!preserveMask)
        {
            PreviewClippingMask = clippingMask;
        }
        else
        {
            clippingMask?.Dispose();
        }
        OnPropertyChanged(nameof(DisplayClippingStats));
        OnPropertyChanged(nameof(IsClippingStatsAvailable));
        OnPropertyChanged(nameof(VisibleClippingOverlaySides));
    }

    private void ClearPreviewClippingArtifacts()
    {
        ApplyPreviewClipping(null, null);
    }

    private void LeaveDevelopClippingSurface()
    {
        _peekClippingSide = ClippingOverlaySide.None;
        RejectPendingClippingMasks();
        ClearPreviewClippingArtifacts();
        OnPropertyChanged(nameof(VisibleClippingOverlaySides));
        ToggleClippingOverlayCommand.NotifyCanExecuteChanged();
    }

    private void NotifyClippingCommandState() =>
        ToggleClippingOverlayCommand.NotifyCanExecuteChanged();
}

using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _clippingOverlayCts;
    private ClippingMask? _previewClippingMask;
    private ClippingOverlaySide _peekClippingSide;

    public ClippingStats? DisplayClippingStats { get; private set; }
    public bool DisplayClippingIsRawSource { get; private set; }
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
                ? DisplayClippingIsRawSource
                    ? ClippingOverlaySide.Both
                    : ClippingOverlaySide.DisplayFloor
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
                return IsHighlightHandlingEnabled
                    ? ClippingOverlaySide.Both
                    : ClippingOverlaySide.DisplayFloor;
            }
            if (_peekClippingSide == ClippingOverlaySide.SceneHighlights &&
                !IsHighlightHandlingEnabled)
            {
                return ClippingOverlaySide.None;
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
            side is not (ClippingOverlaySide.SceneHighlights or
                ClippingOverlaySide.DisplayFloor) ||
            side == ClippingOverlaySide.SceneHighlights &&
                !DisplayClippingIsRawSource)
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
            PreviewClippingMask = null;
        }
    }

    private void RequestClippingOverlayRender()
    {
        var debounce = ReplaceDebounce(ref _clippingOverlayCts);
        _ = RenderClippingOverlayAsync(debounce.Token);
    }

    private async Task RenderClippingOverlayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await UpdatePreviewWithCurrentSliders(
                skipHistogram: true,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void RejectPendingClippingMasks()
    {
        _clippingOverlayCts?.Cancel();
    }

    private void InstallPreviewClipping(PreviewArtifacts artifacts)
    {
        DisplayClippingStats = artifacts.Clipping;
        DisplayClippingIsRawSource = artifacts.IsRawSource;
        PreviewClippingMask = RequestedClippingOverlaySides ==
                ClippingOverlaySide.None
            ? null
            : artifacts.DetachClippingMask();
        OnPropertyChanged(nameof(DisplayClippingStats));
        OnPropertyChanged(nameof(DisplayClippingIsRawSource));
        OnPropertyChanged(nameof(IsClippingStatsAvailable));
        OnPropertyChanged(nameof(VisibleClippingOverlaySides));
    }

    private void ClearPreviewClippingArtifacts()
    {
        DisplayClippingStats = null;
        DisplayClippingIsRawSource = false;
        PreviewClippingMask = null;
        OnPropertyChanged(nameof(DisplayClippingStats));
        OnPropertyChanged(nameof(DisplayClippingIsRawSource));
        OnPropertyChanged(nameof(IsClippingStatsAvailable));
        OnPropertyChanged(nameof(VisibleClippingOverlaySides));
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

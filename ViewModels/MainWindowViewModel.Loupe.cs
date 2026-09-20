using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _loupeLoadingCts;
    private Task? _loupeLoadingTask;
    private Task? _loupeThumbnailTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowseGridVisible))]
    private bool _isLoupeMode;

    [ObservableProperty]
    private ComparePaneViewModel? _loupePane;

    internal Task LoupeLoadingTask => _loupeLoadingTask ?? Task.CompletedTask;

    [RelayCommand]
    private void EnterLoupe()
    {
        if (!IsBrowseGridVisible || SelectedImage == null) return;

        ArmFullScreenSelection();
        IsLoupeMode = true;
        RequestZoomFit?.Invoke();
        ReloadLoupe(SelectedImage);
        UpdateThumbnailPumpAdmission();
        NotifyImageNavigationCommandState();
        NotifyCompareGateChanged();
    }

    [RelayCommand]
    private void ExitLoupe() => CloseLoupe();

    [RelayCommand]
    private void ToggleLoupe()
    {
        if (IsLoupeMode) CloseLoupe();
        else EnterLoupe();
    }

    private void ReloadLoupe(Models.ImageFile? image)
    {
        if (!IsLoupeMode || image == null) return;

        var cancellation = Interlocked.Exchange(ref _loupeLoadingCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        if (LoupePane != null) DisposePreviewPane(LoupePane);

        var pane = new ComparePaneViewModel(image)
        {
            DisplayTransform = DisplayTransform,
            OriginalViewPixelSize = RenderGeometry.CalculateOriginalViewSize(
                image.PixelWidth,
                image.PixelHeight,
                image.EditSettings)
        };
        LoupePane = pane;
        _loupeLoadingCts = new CancellationTokenSource();
        // The thumbnail pump is paused in the loupe, so an image beyond the
        // grid's window has no thumbnail, and that thumbnail is all the pane
        // can show while its preview loads. Fetch it directly.
        if (image.Thumbnail == null)
        {
            _loupeThumbnailTask = TrackDirectThumbnailOperation(
                LoadLoupeThumbnailAsync(
                    _loupeThumbnailTask, image, _loupeLoadingCts.Token));
        }
        Func<bool> isActive = () => IsLoupeMode && ReferenceEquals(LoupePane, pane);
        _loupeLoadingTask = WarmAfterLoupeLoadAsync(
            LoadPreviewPaneAfterAsync(
                _loupeLoadingTask, pane, isActive, _loupeLoadingCts.Token),
            pane, isActive, _loupeLoadingCts.Token);
    }

    // Serialized like the pane loads, latest selection wins: a source read
    // cannot see cancellation until it returns, so holding an arrow key must
    // not stack them. Loupe steps never move the grid window that enforces
    // the thumbnail budget either, so each load settles the budget itself,
    // keeping the selected image and the neighbors the walk is about to visit.
    private async Task LoadLoupeThumbnailAsync(
        Task? previous,
        Models.ImageFile image,
        CancellationToken cancellationToken)
    {
        if (previous != null)
        {
            await previous.ConfigureAwait(
                ConfigureAwaitOptions.ContinueOnCapturedContext |
                ConfigureAwaitOptions.SuppressThrowing);
        }
        if (cancellationToken.IsCancellationRequested || !IsLoupeMode ||
            !ReferenceEquals(SelectedImage, image) || image.Thumbnail != null)
            return;
        await RefreshSelectedThumbnailAsync(image, cancellationToken);
        if (!IsLoupeMode || !ReferenceEquals(SelectedImage, image)) return;
        _thumbnailLastAccess[image] = ++_thumbnailAccessClock;
        ReserveThumbnailResidency(UpcomingAdjacentWarmCandidates());
    }

    private async Task WarmAfterLoupeLoadAsync(
        Task load,
        ComparePaneViewModel pane,
        Func<bool> isActive,
        CancellationToken cancellationToken)
    {
        await load;
        // A refinement queued behind this load schedules the warm itself.
        if (isActive() && !cancellationToken.IsCancellationRequested &&
            pane.Preview != null && !pane.IsRefinementQueued)
        {
            ScheduleAdjacentPreviewWarm(pane.Image);
        }
    }

    internal void PublishLoupeRequiredDeviceLongEdge(int longEdge, bool isLoupePeekActive)
    {
        if (!IsLoupeMode || LoupePane is not { } pane) return;

        pane.RequiredDeviceLongEdge = Math.Max(0, longEdge);
        pane.IsLoupeRefinementRequested = isLoupePeekActive || !IsZoomFitMode;
        if (!pane.IsLoupeRefinementRequested)
        {
            RestorePreviewPane(pane);
            return;
        }
        if (!PreviewPaneNeedsRefinement(pane) || pane.IsRefinementQueued ||
            _loupeLoadingCts is not { } cancellation)
        {
            return;
        }

        pane.IsRefinementQueued = true;
        // A 1:1 refinement is a full decode; speculative work waits for it.
        CancelAdjacentPreviewWarm(invalidateWorker: true);
        Func<bool> isActive = () => IsLoupeMode && ReferenceEquals(LoupePane, pane);
        _loupeLoadingTask = WarmAfterLoupeLoadAsync(
            LoadPreviewPaneRefinementAfterAsync(
                _loupeLoadingTask, pane, isActive, cancellation.Token),
            pane, isActive, cancellation.Token);
    }

    private void CloseLoupe()
    {
        if (!IsLoupeMode && LoupePane == null) return;

        var cancellation = Interlocked.Exchange(ref _loupeLoadingCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        IsLoupeMode = false;
        CancelAdjacentPreviewWarm(invalidateWorker: true);
        if (LoupePane != null) DisposePreviewPane(LoupePane);
        LoupePane = null;
        ReleaseFullScreenSelection();
        UpdateThumbnailPumpAdmission();
        NotifyImageNavigationCommandState();
        NotifyCompareGateChanged();
    }

    private async Task CancelAndDrainLoupeAsync()
    {
        CloseLoupe();
        var loading = Interlocked.Exchange(ref _loupeLoadingTask, null);
        if (loading != null)
            await loading.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}

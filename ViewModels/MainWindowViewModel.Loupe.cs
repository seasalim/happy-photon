using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _loupeLoadingCts;
    private Task? _loupeLoadingTask;

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
        _loupeLoadingTask = LoadPreviewPaneAfterAsync(
            _loupeLoadingTask, pane,
            () => IsLoupeMode && ReferenceEquals(LoupePane, pane),
            _loupeLoadingCts.Token);
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
        _loupeLoadingTask = LoadPreviewPaneRefinementAfterAsync(
            _loupeLoadingTask, pane,
            () => IsLoupeMode && ReferenceEquals(LoupePane, pane),
            cancellation.Token);
    }

    private void CloseLoupe()
    {
        if (!IsLoupeMode && LoupePane == null) return;

        var cancellation = Interlocked.Exchange(ref _loupeLoadingCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        IsLoupeMode = false;
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

using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private long _activeBaseRefreshRequestId;

    private void OnPreviewRefreshed(object? sender, PreviewRefresh refresh)
    {
        var outcome = RenderOutcome.FromRefresh(
            refresh,
            refresh.DetachBitmap(),
            refresh.DetachClippingMask(),
            refresh.DetachPromotionLease(),
            PreviewSurfaceIntent.Edited);
        Dispatcher.UIThread.Post(() =>
        {
            // Requested intent belongs to the UI thread. A refresh preserves
            // whatever intent is current when its outcome is actually applied.
            outcome.Intent = _requestedPreviewIntent;
            var image = outcome.Image;
            ApplyRenderOutcome(outcome);
            if (image != null)
            {
                _ = TrackDirectThumbnailOperation(RefreshThumbnailAsync(image));
            }
        });
    }

    internal void ApplyPreviewRefresh(
        ImageFile imageFile,
        Bitmap bitmap,
        HistogramData histogram,
        bool hasHistogram,
        HistogramData? rawHistogram,
        long generation,
        ClippingStats? clipping = null,
        bool? isRawSource = null,
        DcpProfileState? profileState = null,
        ClippingMask? clippingMask = null,
        bool isMonochrome = false)
    {
        using var refresh = new PreviewRefresh(
            imageFile,
            bitmap,
            histogram,
            hasHistogram,
            generation,
            rawHistogram,
            clipping,
            isRawSource ?? imageFile.IsRaw,
            profileState,
            clippingMask,
            isMonochrome: isMonochrome);
        ApplyRenderOutcome(RenderOutcome.FromRefresh(
            refresh,
            refresh.DetachBitmap(),
            refresh.DetachClippingMask(),
            promotionLease: null,
            _requestedPreviewIntent));
    }

    private void OnBaseRefreshStateChanged(
        object? sender,
        PreviewBaseRefreshState state) =>
        Dispatcher.UIThread.Post(() => ApplyBaseRefreshState(state));

    internal void ApplyBaseRefreshState(PreviewBaseRefreshState state)
    {
        if (!ReferenceEquals(SelectedImage, state.ImageFile))
        {
            return;
        }

        RefreshLocalRangeMask();
        if (state.IsRefreshing)
        {
            Volatile.Write(
                ref _activeBaseRefreshRequestId,
                state.RequestId);
            if (IsWorkspacePreviewSurfaceActive || IsFullScreenMode)
            {
                ApplyRenderOutcome(new RenderOutcome
                {
                    Image = state.ImageFile,
                    Generation = Volatile.Read(
                        ref _latestPreviewOutcomeGeneration),
                    Class = RenderOutcomeClass.StateDefining,
                    Intent = _requestedPreviewIntent,
                    ClippingMode = OutcomeFieldMode.Clear,
                    RawHistogramMode = OutcomeFieldMode.Clear
                });
            }
            return;
        }

        if (Volatile.Read(ref _activeBaseRefreshRequestId) ==
            state.RequestId)
        {
            Volatile.Write(ref _activeBaseRefreshRequestId, 0);
            NotifyRawHistogramState();
        }
    }
}

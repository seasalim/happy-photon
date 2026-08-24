using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _adjacentWarmCts;
    private int _adjacentWarmDirection = 1;
    private void UpdateAdjacentWarmDirection(ImageFile? oldImage, ImageFile? newImage)
    {
        var oldIndex = oldImage == null ? -1 : Browse.VisibleImages.IndexOf(oldImage);
        var newIndex = newImage == null ? -1 : Browse.VisibleImages.IndexOf(newImage);
        if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
            _adjacentWarmDirection = newIndex < oldIndex ? -1 : 1;
    }
    private void ScheduleAdjacentPreviewWarm(PreviewRenderIdentity parent)
    {
        CancelAdjacentPreviewWarm(invalidateWorker: false);
        if (!IsDevelopMode || IsFullScreenMode || SelectedImage == null) return;
        var candidate = Browse.MoveVisible(SelectedImage, _adjacentWarmDirection);
        if (candidate == null) return;
        var cancellation = new CancellationTokenSource();
        _adjacentWarmCts = cancellation;
        _ = DebouncedAction.RunAsync(
            "adjacent preview warm",
            RestingSettleDelay,
            cancellation.Token,
            () => StartAdjacentPreviewWarm(parent, cancellation),
            timeProvider: _timeProvider);
    }
    private async Task StartAdjacentPreviewWarm(
        PreviewRenderIdentity parent,
        CancellationTokenSource cancellation)
    {
        while (ReferenceEquals(_adjacentWarmCts, cancellation) &&
               ReferenceEquals(SelectedImage, parent.ImageFile) &&
               IsDevelopMode && !IsFullScreenMode)
        {
            var candidate = Browse.MoveVisible(
                SelectedImage, _adjacentWarmDirection);
            if (candidate == null || ImageService.Previews.TryStartAdjacentWarm(
                    candidate, out var blockingWorker)) return;
            if (blockingWorker == null) return;
            await blockingWorker.WaitAsync(cancellation.Token);
        }
    }
    private void CancelAdjacentPreviewWarm(
        bool invalidateWorker,
        bool dropRetained = false,
        ImageFile? imageFile = null)
    {
        CancelAndDispose(ref _adjacentWarmCts);
        if (invalidateWorker && _imageService.IsValueCreated)
            _imageService.Value.Previews.InvalidateAdjacentWarm(
                imageFile, dropRetained);
    }
}

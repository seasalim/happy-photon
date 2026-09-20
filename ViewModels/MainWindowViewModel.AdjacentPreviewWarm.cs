using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _adjacentWarmCts;
    private int _adjacentWarmDirection = 1;
    private void UpdateAdjacentWarmDirection(ImageFile? oldImage, ImageFile? newImage)
    {
        var oldRepresentative = VisibleRepresentative(oldImage);
        var newRepresentative = VisibleRepresentative(newImage);
        var oldIndex = oldRepresentative == null
            ? -1
            : Browse.VisibleImages.IndexOf(oldRepresentative);
        var newIndex = newRepresentative == null
            ? -1
            : Browse.VisibleImages.IndexOf(newRepresentative);
        if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
            _adjacentWarmDirection = newIndex < oldIndex ? -1 : 1;
    }
    private bool IsAdjacentWarmSurfaceActive =>
        (IsDevelopMode || IsLoupeMode) && !IsFullScreenMode;
    private void ScheduleAdjacentPreviewWarm(ImageFile parent)
    {
        CancelAdjacentPreviewWarm(invalidateWorker: false);
        if (!IsAdjacentWarmSurfaceActive || SelectedImage == null) return;
        if (AdjacentWarmCandidate(_adjacentWarmDirection) == null) return;
        var cancellation = new CancellationTokenSource();
        _adjacentWarmCts = cancellation;
        _ = DebouncedAction.RunAsync(
            "adjacent preview warm",
            RestingSettleDelay,
            cancellation.Token,
            () => StartAdjacentPreviewWarm(parent, cancellation),
            timeProvider: _timeProvider);
    }
    // One worker walks ahead in the travel direction, one neighbor at a time,
    // so lingering on an image fills a buffer that a burst of steps drains.
    private const int AdjacentWarmDepth = 5;
    private async Task StartAdjacentPreviewWarm(
        ImageFile parent,
        CancellationTokenSource cancellation)
    {
        CullPerf?.Record("BufferRefill", parent.CatalogId);
        for (var offset = 1; offset <= AdjacentWarmDepth; offset++)
        {
            while (true)
            {
                if (!ReferenceEquals(_adjacentWarmCts, cancellation) ||
                    !ReferenceEquals(SelectedImage, parent) ||
                    !IsAdjacentWarmSurfaceActive)
                    return;
                var candidate = AdjacentWarmCandidate(
                    _adjacentWarmDirection * offset);
                if (candidate == null) return;
                var active = ImageService.Previews.ActiveAdjacentWarm;
                if (active != null)
                {
                    await active.WaitAsync(cancellation.Token);
                    continue;
                }
                if (ImageService.Previews.TryStartAdjacentWarm(
                        candidate, out var blockingWorker) ||
                    blockingWorker == null)
                    break;
                await blockingWorker.WaitAsync(cancellation.Token);
            }
        }
        CullPerf?.Record("WalkComplete", parent.CatalogId);
    }
    // Arrow keys in the loupe walk the armed selection, so the warm must too.
    private ImageFile? AdjacentWarmCandidate(int offset)
    {
        if (!_isFullScreenSelectionRestricted)
            return Browse.MoveVisible(VisibleRepresentative(SelectedImage), offset);
        var members = GetFullScreenSelectionMembers();
        var index = members.IndexOf(SelectedImage!);
        var target = index + offset;
        return index >= 0 && target >= 0 && target < members.Count
            ? members[target]
            : null;
    }
    private void CancelAdjacentPreviewWarm(
        bool invalidateWorker,
        bool dropRetained = false,
        ImageFile? imageFile = null,
        ImageFile? joinFor = null,
        IReadOnlyList<ImageFile>? keepAheadFor = null)
    {
        CancelAndDispose(ref _adjacentWarmCts);
        if (invalidateWorker && _imageService.IsValueCreated)
            _imageService.Value.Previews.InvalidateAdjacentWarm(
                imageFile, dropRetained, joinFor, keepAheadFor);
    }
    // The neighbors the next walk from the current selection will want.
    private List<ImageFile> UpcomingAdjacentWarmCandidates()
    {
        var upcoming = new List<ImageFile>(AdjacentWarmDepth);
        for (var offset = 1; offset <= AdjacentWarmDepth; offset++)
        {
            var candidate = AdjacentWarmCandidate(_adjacentWarmDirection * offset);
            if (candidate == null) break;
            upcoming.Add(candidate);
        }
        return upcoming;
    }
}

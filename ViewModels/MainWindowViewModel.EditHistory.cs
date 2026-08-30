using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private const int NavigatorHoverMaxDimension = 280;
    private static readonly TimeSpan HistoryHoverDelay = TimeSpan.FromMilliseconds(80);
    private CancellationTokenSource? _historyHoverCts;
    private EditHistoryEntry? _hoveredHistoryEntry;

    [ObservableProperty]
    private Bitmap? _navigatorHoverImage;

    public async Task PreviewHistoryHoverAsync(EditHistoryEntry entry)
    {
        EndHistoryHover();
        var image = SelectedImage;
        if (image == null || entry.IsCurrent || !IsDevelopMode ||
            IsHistoryBlockedByCrop || _isHoveringPreset || IsBeforeAfterSplit ||
            _isBeforeAfterSplitTransitioning ||
            !IsHistoryLoaded || _history.PositionOf(entry) < 0) return;

        _hoveredHistoryEntry = entry;
        CancelRestingPreview(clearParent: true);
        var cts = new CancellationTokenSource();
        _historyHoverCts = cts;
        await DebouncedAction.RunAsync(
            "history hover", HistoryHoverDelay, cts.Token,
            async () =>
            {
                var result = await ImageService.Previews
                    .RenderCurrentBaseSideSurfaceAsync(
                        image, entry.Settings, NavigatorHoverMaxDimension, cts.Token);
                if (result.Bitmap == null || cts.IsCancellationRequested ||
                    !ReferenceEquals(_historyHoverCts, cts) ||
                    !ReferenceEquals(_hoveredHistoryEntry, entry) ||
                    !ReferenceEquals(SelectedImage, image))
                {
                    result.Bitmap?.Dispose();
                    return;
                }
                var previous = NavigatorHoverImage;
                NavigatorHoverImage = result.Bitmap;
                if (previous != null)
                    _bitmapRetirement.Retire(previous,
                        () => ReferenceEquals(NavigatorHoverImage, previous));
            },
            timeProvider: _timeProvider);
    }

    public void EndHistoryHover()
    {
        var wasHovering = _hoveredHistoryEntry != null;
        CancelHistoryHover();
        if (wasHovering && PreviewImage is { } preview)
            OnAcceptedInteractivePreview(preview, scheduleAdjacentWarm: false);
    }

    private void CancelHistoryHover()
    {
        _hoveredHistoryEntry = null;
        CancelAndDispose(ref _historyHoverCts);
        var bitmap = NavigatorHoverImage;
        NavigatorHoverImage = null;
        if (bitmap != null)
            _bitmapRetirement.Retire(bitmap, () => false);
    }

    private void BeginDevelopHistoryLoad(ImageFile? image)
    {
        var generation = Interlocked.Increment(ref _historySubjectGeneration);
        _history.Clear();
        IsHistoryLoaded = false;
        SyncHistoryFlags();
        _pendingHistoryLoad = IsDevelopMode && image != null
            ? LoadDevelopHistoryAsync(image, generation)
            : null;
    }

    private async Task LoadDevelopHistoryAsync(ImageFile image, long generation)
    {
        CatalogEditHistoryState state;
        try
        {
            await image.EnsureCatalogIdAsync(_catalogService);
            state = await Task.Run(() => _catalogService.LoadEditHistoryAsync(
                image.CatalogId));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Edit history load failed: {ex.Message}");
            return;
        }

        if (!IsCurrentHistorySubject(image, generation)) return;
        _history.Load(state.Entries, state.Position);
        IsHistoryLoaded = true;
        SyncHistoryFlags();
    }

    private bool IsCurrentHistorySubject(ImageFile image, long generation) =>
        generation == Volatile.Read(ref _historySubjectGeneration) &&
        IsDevelopMode &&
        ReferenceEquals(SelectedImage, image);

    private async Task WaitForPendingHistoryWorkAsync()
    {
        while (true)
        {
            var preview = _previewDebounceTask;
            if (preview is { IsCompleted: false })
            {
                await ObservePendingHistoryWorkAsync(preview);
            }

            var commit = _pendingHistoryCommit;
            if (commit is { IsCompleted: false })
            {
                await ObservePendingHistoryWorkAsync(commit);
            }

            if (ReferenceEquals(preview, _previewDebounceTask) &&
                ReferenceEquals(commit, _pendingHistoryCommit))
            {
                return;
            }
        }
    }

    private static async Task ObservePendingHistoryWorkAsync(Task pending)
    {
        try
        {
            await pending;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Pending edit commit failed: {ex.Message}");
        }
    }

    private bool CanJumpToHistoryStep(EditHistoryEntry? entry) =>
        !IsHistoryBlockedByCrop && IsHistoryLoaded && entry != null;

    [RelayCommand(CanExecute = nameof(CanJumpToHistoryStep))]
    private async Task JumpToHistoryStepAsync(EditHistoryEntry? entry)
    {
        var image = SelectedImage;
        var generation = Volatile.Read(ref _historySubjectGeneration);
        if (entry == null || image == null) return;
        await WaitForPendingHistoryWorkAsync();
        if (IsHistoryBlockedByCrop ||
            !IsCurrentHistorySubject(image, generation) || !IsHistoryLoaded)
            return;
        var position = _history.PositionOf(entry);
        var target = _history.EntryAt(position);
        if (target == null || position == _history.Position) return;
        await ApplyHistoryStateAsync(
            image, generation, target.Settings, position);
    }

    private bool CanClearHistoryAboveStep(EditHistoryEntry? entry)
    {
        if (IsHistoryBlockedByCrop || !IsHistoryLoaded || entry == null)
            return false;
        var position = _history.PositionOf(entry);
        return position >= 0 && position < _history.Entries.Count - 1;
    }

    [RelayCommand(CanExecute = nameof(CanClearHistoryAboveStep))]
    private async Task ClearHistoryAboveStepAsync(EditHistoryEntry? entry)
    {
        var image = SelectedImage;
        var generation = Volatile.Read(ref _historySubjectGeneration);
        if (entry == null || image == null) return;
        await WaitForPendingHistoryWorkAsync();
        if (IsHistoryBlockedByCrop ||
            !IsCurrentHistorySubject(image, generation) || !IsHistoryLoaded)
            return;
        var position = _history.PositionOf(entry);
        var target = _history.EntryAt(position);
        if (target == null || position >= _history.Entries.Count - 1) return;
        await ApplyHistoryStateAsync(
            image, generation, target.Settings, position,
            new CatalogEditHistoryMutation(position, [], position));
    }

    private bool CanClearHistory() =>
        !IsHistoryBlockedByCrop && IsHistoryLoaded &&
        _history.Entries.Count > 1;

    [RelayCommand(CanExecute = nameof(CanClearHistory))]
    private async Task ClearHistoryAsync()
    {
        var image = SelectedImage;
        var generation = Volatile.Read(ref _historySubjectGeneration);
        if (image == null) return;
        await WaitForPendingHistoryWorkAsync();
        if (IsHistoryBlockedByCrop ||
            !IsCurrentHistorySubject(image, generation) || !IsHistoryLoaded ||
            _history.Entries.Count == 0)
        {
            return;
        }
        var clear = ClearHistoryCoreAsync(image, generation);
        _serializedHistoryCommit = clear;
        _serializedHistoryImage = image;
        _serializedHistorySettings = image.EditSettings.Clone();
        TrackHistoryCommit(clear);
        await clear;
    }

    private async Task ClearHistoryCoreAsync(ImageFile image, long generation)
    {
        await _catalogService.ClearEditHistoryAsync(image.CatalogId);
        if (!IsCurrentHistorySubject(image, generation)) return;
        _history.Clear();
        SyncHistoryFlags();
    }
}

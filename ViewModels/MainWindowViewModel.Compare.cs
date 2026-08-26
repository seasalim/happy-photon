using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? _compareLoadingCts;
    private Task? _compareLoadingTask;
    private List<ImageFile> _compareSelectionSnapshot = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowseGridVisible))]
    private bool _isCompareMode;

    public bool IsBrowseGridVisible => IsBrowseMode && !IsCompareMode;
    public bool CanEnterCompare =>
        IsBrowseGridVisible && Browse.SelectedCount is >= 2 and <= 4;
    public bool CanToggleCompare => IsCompareMode || CanEnterCompare;
    public string CompareViewToolTip => IsCompareMode
        ? "Return to grid"
        : CanEnterCompare
            ? "Compare (2–4 images)"
            : "Select 2–4 images to compare";
    public ObservableCollection<ComparePaneViewModel> ComparePanes { get; } = [];
    public SynchronizedViewService SynchronizedView { get; } = new();
    internal Task CompareLoadingTask =>
        _compareLoadingTask ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanEnterCompare))]
    private void EnterCompare()
    {
        if (!CanEnterCompare) return;

        var members = Browse.VisibleImages
            .Where(image => image.IsSelected)
            .ToArray();
        if (members.Length is < 2 or > 4) return;

        _compareSelectionSnapshot = members.ToList();
        foreach (var member in members)
        {
            ComparePanes.Add(new ComparePaneViewModel(member)
            {
                OriginalViewPixelSize =
                    RenderGeometry.CalculateOriginalViewSize(
                        member.PixelWidth,
                        member.PixelHeight,
                        member.EditSettings)
            });
        }
        IsCompareMode = true;
        if (SelectedImage == null || !members.Contains(SelectedImage))
        {
            SelectedImage = members[0];
        }
        SynchronizedView.Reset();
        UpdateThumbnailPumpAdmission();
        NotifyImageNavigationCommandState();
        _compareLoadingCts = new CancellationTokenSource();
        _compareLoadingTask = LoadComparePanesAfterAsync(
            _compareLoadingTask,
            _compareLoadingCts.Token);
        NotifyCompareGateChanged();
    }

    [RelayCommand]
    private void ExitCompare() => CloseCompare();

    [RelayCommand(CanExecute = nameof(CanToggleCompare))]
    private void ToggleCompare()
    {
        if (IsCompareMode)
        {
            CloseCompare();
        }
        else
        {
            EnterCompare();
        }
    }

    [RelayCommand]
    private void ActivateComparePane(ComparePaneViewModel? pane)
    {
        if (IsCompareMode && pane != null && ComparePanes.Contains(pane))
        {
            SelectedImage = pane.Image;
        }
    }

    private async Task LoadComparePanesAsync(CancellationToken cancellationToken)
    {
        foreach (var pane in ComparePanes.ToArray())
        {
            try
            {
                using var cached = await ImageService.Previews.LoadCachedPreviewAsync(
                    pane.Image,
                    pane.Image.EditSettings,
                    cancellationToken);
                if (cached != null)
                {
                    var cachedSize = cached.SettingsMatch
                        ? cached.OriginalViewPixelSize
                        : null;
                    ApplyCompareBitmap(pane, cached.DetachBitmap());
                    if (cachedSize is { Width: > 0, Height: > 0 })
                    {
                        pane.OriginalViewPixelSize = cachedSize.Value;
                        continue;
                    }
                }

                using var fresh = await ImageService.Previews.LoadComparePreviewAsync(
                    pane.Image,
                    pane.Image.EditSettings,
                    cancellationToken: cancellationToken);
                if (fresh != null)
                {
                    ApplyCompareBitmap(
                        pane,
                        fresh.DetachBitmap(),
                        fresh.OriginalViewPixelSize);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Compare preview failed for {pane.Image.FilePath}: {exception.Message}");
            }
            finally
            {
                pane.IsLoading = false;
            }
        }
    }

    private async Task LoadComparePanesAfterAsync(
        Task? previous,
        CancellationToken cancellationToken)
    {
        if (previous != null)
        {
            await previous.ConfigureAwait(
                ConfigureAwaitOptions.ContinueOnCapturedContext |
                ConfigureAwaitOptions.SuppressThrowing);
        }
        cancellationToken.ThrowIfCancellationRequested();
        await LoadComparePanesAsync(cancellationToken);
    }

    internal void PublishCompareRequiredDeviceLongEdge(
        ComparePaneViewModel pane,
        int longEdge,
        bool isLoupePeekActive)
    {
        if (!IsCompareMode || !ComparePanes.Contains(pane)) return;

        pane.RequiredDeviceLongEdge = Math.Max(0, longEdge);
        pane.IsLoupeRefinementRequested = isLoupePeekActive;
        if (!isLoupePeekActive)
        {
            RestoreComparePreview(pane);
            return;
        }
        if (!ComparePaneNeedsRefinement(pane) ||
            pane.IsRefinementQueued ||
            _compareLoadingCts is not { } cancellation)
        {
            return;
        }

        pane.IsRefinementQueued = true;
        _compareLoadingTask = LoadCompareRefinementAfterAsync(
            _compareLoadingTask,
            pane,
            cancellation.Token);
    }

    private async Task LoadCompareRefinementAfterAsync(
        Task? previous,
        ComparePaneViewModel pane,
        CancellationToken cancellationToken)
    {
        if (previous != null)
        {
            await previous.ConfigureAwait(
                ConfigureAwaitOptions.ContinueOnCapturedContext |
                ConfigureAwaitOptions.SuppressThrowing);
        }

        try
        {
            while (IsCompareMode && ComparePanes.Contains(pane) &&
                   ComparePaneNeedsRefinement(pane))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requested = pane.RequiredDeviceLongEdge;
                using var refined =
                    await ImageService.Previews.LoadComparePreviewAsync(
                        pane.Image,
                        pane.Image.EditSettings,
                        requested,
                        cancellationToken);
                if (refined == null) return;
                if (!ComparePaneNeedsRefinement(pane))
                {
                    return;
                }
                ApplyCompareBitmap(
                    pane,
                    refined.DetachBitmap(),
                    refined.OriginalViewPixelSize,
                    isRefinement: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Compare refinement failed for {pane.Image.FilePath}: " +
                exception.Message);
        }
        finally
        {
            pane.IsRefinementQueued = false;
        }
    }

    private static bool ComparePaneNeedsRefinement(ComparePaneViewModel pane) =>
        pane.IsLoupeRefinementRequested &&
        pane.RequiredDeviceLongEdge > pane.RenderedLongEdge &&
        (pane.AchievableLongEdge == 0 ||
         pane.RenderedLongEdge < pane.AchievableLongEdge);

    private void ApplyCompareBitmap(
        ComparePaneViewModel pane,
        Bitmap bitmap,
        Avalonia.PixelSize originalViewPixelSize = default,
        bool isRefinement = false)
    {
        if (!IsCompareMode || !ComparePanes.Contains(pane))
        {
            bitmap.Dispose();
            return;
        }

        var previous = pane.Preview;
        pane.Preview = bitmap;
        pane.RenderedLongEdge = Math.Max(
            bitmap.PixelSize.Width,
            bitmap.PixelSize.Height);
        if (!isRefinement)
        {
            var previousPreviewResolution = pane.PreviewResolutionBitmap;
            pane.PreviewResolutionBitmap = bitmap;
            pane.PreviewResolutionLongEdge = pane.RenderedLongEdge;
            if (previousPreviewResolution != null &&
                !ReferenceEquals(previousPreviewResolution, previous))
            {
                RetireCompareBitmap(pane, previousPreviewResolution);
            }
        }
        if (originalViewPixelSize.Width > 0 && originalViewPixelSize.Height > 0)
        {
            pane.OriginalViewPixelSize = originalViewPixelSize;
            pane.AchievableLongEdge = Math.Max(
                originalViewPixelSize.Width,
                originalViewPixelSize.Height);
        }
        if (previous != null &&
            !ReferenceEquals(previous, pane.PreviewResolutionBitmap))
        {
            RetireCompareBitmap(pane, previous);
        }
    }

    private void RestoreComparePreview(ComparePaneViewModel pane)
    {
        var preview = pane.PreviewResolutionBitmap;
        if (preview == null || ReferenceEquals(pane.Preview, preview))
        {
            return;
        }

        var refinement = pane.Preview;
        pane.Preview = preview;
        pane.RenderedLongEdge = pane.PreviewResolutionLongEdge;
        if (refinement != null) RetireCompareBitmap(pane, refinement);
    }

    private void RetireCompareBitmap(
        ComparePaneViewModel pane,
        Bitmap bitmap) =>
        _bitmapRetirement.Retire(
            bitmap,
            () => ReferenceEquals(pane.Preview, bitmap) ||
                ReferenceEquals(pane.PreviewResolutionBitmap, bitmap));

    private void CloseCompare()
    {
        if (!IsCompareMode && ComparePanes.Count == 0) return;

        var cancellation = Interlocked.Exchange(ref _compareLoadingCts, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        IsCompareMode = false;
        foreach (var pane in ComparePanes)
        {
            var bitmap = pane.Preview;
            var previewResolution = pane.PreviewResolutionBitmap;
            pane.Preview = null;
            pane.PreviewResolutionBitmap = null;
            if (bitmap != null) _bitmapRetirement.Retire(bitmap, () => false);
            if (previewResolution != null &&
                !ReferenceEquals(previewResolution, bitmap))
            {
                _bitmapRetirement.Retire(previewResolution, () => false);
            }
        }
        ComparePanes.Clear();

        var retained = _compareSelectionSnapshot.ToHashSet(
            ReferenceEqualityComparer.Instance);
        foreach (var image in Browse.AllImages)
        {
            image.IsSelected = retained.Contains(image);
        }
        _compareSelectionSnapshot = [];
        UpdateSelectedCount();
        UpdateThumbnailPumpAdmission();
        NotifyImageNavigationCommandState();
        NotifyCompareGateChanged();
    }

    private async Task CancelAndDrainCompareAsync()
    {
        CloseCompare();
        var loading = Interlocked.Exchange(ref _compareLoadingTask, null);
        if (loading != null)
        {
            await loading.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private bool TryMoveWithinCompareSet(int offset)
    {
        if (!IsCompareMode) return false;

        var current = SelectedImage == null
            ? -1
            : _compareSelectionSnapshot.IndexOf(SelectedImage);
        if (current < 0) current = 0;
        var destination = Math.Clamp(
            current + offset,
            0,
            _compareSelectionSnapshot.Count - 1);
        SelectedImage = _compareSelectionSnapshot[destination];
        return true;
    }

    // The pane grid is two columns wide, so one row is two panes.
    private const int CompareColumnCount = 2;

    private bool TryMoveWithinCompareRow(int rowOffset)
    {
        if (!IsCompareMode) return false;

        var current = SelectedImage == null
            ? -1
            : _compareSelectionSnapshot.IndexOf(SelectedImage);
        if (current < 0) current = 0;
        // Unlike a horizontal step this deliberately does not clamp: there is no
        // pane below the bottom row, and clamping would slide the ring sideways
        // instead of leaving it where it is.
        var destination = current + rowOffset * CompareColumnCount;
        if (destination >= 0 && destination < _compareSelectionSnapshot.Count)
        {
            SelectedImage = _compareSelectionSnapshot[destination];
        }
        return true;
    }

    private List<ImageFile> GetCompareMembers() =>
        _compareSelectionSnapshot;

    private void NotifyCompareGateChanged()
    {
        OnPropertyChanged(nameof(CanEnterCompare));
        OnPropertyChanged(nameof(CanToggleCompare));
        OnPropertyChanged(nameof(CompareViewToolTip));
        EnterCompareCommand.NotifyCanExecuteChanged();
        ToggleCompareCommand.NotifyCanExecuteChanged();
    }

}

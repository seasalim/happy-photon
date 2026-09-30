using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private long _cullOperation;
    private CullPerfRecorder? CullPerf =>
        _imageService.IsValueCreated ? _imageService.Value.Previews.CullPerf : null;

    [RelayCommand(CanExecute = nameof(CanSelectPreviousImage))]
    private void SelectPreviousImage()
    {
        _cullOperation = CullPerf?.Record(CanSelectPreviousImage() ? "Receipt" : "NoOp", operation: -1) ?? 0;
        if (TryMoveWithinCompareSet(-1)) return;
        if (TryMoveWithinFullScreenSelection(-1)) return;
        if (TryMoveWithinExportSelection(-1)) return;
        MoveFocusAndSelection(Browse.PreviousVisible(
            VisibleRepresentative(SelectedImage)));
    }

    [RelayCommand(CanExecute = nameof(CanSelectNextImage))]
    private void SelectNextImage()
    {
        _cullOperation = CullPerf?.Record(CanSelectNextImage() ? "Receipt" : "NoOp", operation: -1) ?? 0;
        if (TryMoveWithinCompareSet(1)) return;
        if (TryMoveWithinFullScreenSelection(1)) return;
        if (TryMoveWithinExportSelection(1)) return;
        MoveFocusAndSelection(Browse.NextVisible(
            VisibleRepresentative(SelectedImage)));
    }

    private bool CanSelectPreviousImage() => NavigationPosition().Index > 0;

    private bool CanSelectNextImage()
    {
        var position = NavigationPosition();
        return position.Index >= 0 && position.Index < position.Count - 1;
    }

    private (int Index, int Count) NavigationPosition()
    {
        IList<ImageFile> images = IsCompareMode
            ? GetCompareMembers()
            : IsFullScreenSelectionRestricted
                ? GetFullScreenSelectionMembers()
                : Browse.VisibleImages;
        var active = IsCompareMode || IsFullScreenSelectionRestricted
            ? SelectedImage
            : VisibleRepresentative(SelectedImage);
        return (active == null ? -1 : images.IndexOf(active), images.Count);
    }

    private void NotifyImageNavigationCommandState()
    {
        SelectPreviousImageCommand.NotifyCanExecuteChanged();
        SelectNextImageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Navigate up by the specified number of items (one row in grid view).
    /// </summary>
    public void SelectImageUp(int itemsPerRow)
    {
        if (TryMoveWithinCompareRow(-1)) return;
        if (TryMoveWithinFullScreenSelection(-itemsPerRow)) return;
        MoveFocusAndSelection(Browse.MoveVisible(
            VisibleRepresentative(SelectedImage), -itemsPerRow));
    }

    /// <summary>
    /// Navigate down by the specified number of items (one row in grid view).
    /// </summary>
    public void SelectImageDown(int itemsPerRow)
    {
        if (TryMoveWithinCompareRow(1)) return;
        if (TryMoveWithinFullScreenSelection(itemsPerRow)) return;
        MoveFocusAndSelection(Browse.MoveVisible(
            VisibleRepresentative(SelectedImage), itemsPerRow));
    }

    public void SelectFirstImage()
    {
        if (TrySelectFullScreenSelectionBoundary(last: false)) return;

        SelectedImage = Browse.FirstVisible();
        if (SelectedImage != null) MoveSelectionWithFocus(SelectedImage);
    }

    public void SelectLastImage()
    {
        if (TrySelectFullScreenSelectionBoundary(last: true)) return;

        SelectedImage = Browse.LastVisible();
        if (SelectedImage != null) MoveSelectionWithFocus(SelectedImage);
    }

    /// <summary>
    /// Shift+navigation in the Browse grid: move focus by <paramref name="offset"/>
    /// and select exactly the span from the anchor to the new focus.
    /// </summary>
    /// <returns>The anchor used, for the caller to keep.</returns>
    public ImageFile? ExtendSelection(ImageFile? anchor, int offset) =>
        ExtendSelectionTo(anchor, Browse.MoveVisible(
            VisibleRepresentative(SelectedImage), offset));

    public ImageFile? ExtendSelectionToEdge(ImageFile? anchor, bool last) =>
        ExtendSelectionTo(anchor, last ? Browse.LastVisible() : Browse.FirstVisible());

    private ImageFile? ExtendSelectionTo(ImageFile? anchor, ImageFile? target)
    {
        if (!IsBrowseGridVisible || IsFullScreenMode)
        {
            MoveFocusAndSelection(target);
            return target ?? anchor;
        }

        anchor = VisibleRepresentative(anchor);
        if (anchor == null || !Browse.VisibleImages.Contains(anchor))
            anchor = VisibleRepresentative(SelectedImage);

        if (target == null || anchor == null) return anchor;

        SelectedImage = target;
        Browse.SelectOnlyRange(anchor, target);
        UpdateSelectedCount();
        return anchor;
    }

    private void MoveFocusAndSelection(ImageFile? image)
    {
        if (image == null) return;

        SelectedImage = image;
        MoveSelectionWithFocus(image);
    }

    // Keyboard navigation in the Browse grid carries the selection with the
    // focused image so assessment actions land on the photo under the ring.
    private void MoveSelectionWithFocus(ImageFile image)
    {
        if (!IsBrowseMode || IsFullScreenMode || IsCompareMode ||
            IsLoupeMode && IsFullScreenSelectionRestricted) return;

        Browse.SelectOnly(image);
        UpdateSelectedCount();
    }
}

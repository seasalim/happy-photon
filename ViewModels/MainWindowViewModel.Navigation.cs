using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanSelectPreviousImage))]
    private void SelectPreviousImage()
    {
        if (TryMoveWithinFullScreenSelection(-1)) return;
        MoveFocusAndSelection(Browse.PreviousVisible(SelectedImage));
    }

    [RelayCommand(CanExecute = nameof(CanSelectNextImage))]
    private void SelectNextImage()
    {
        if (TryMoveWithinFullScreenSelection(1)) return;
        MoveFocusAndSelection(Browse.NextVisible(SelectedImage));
    }

    private bool CanSelectPreviousImage() => NavigationPosition().Index > 0;

    private bool CanSelectNextImage()
    {
        var position = NavigationPosition();
        return position.Index >= 0 && position.Index < position.Count - 1;
    }

    private (int Index, int Count) NavigationPosition()
    {
        IList<ImageFile> images = IsFullScreenSelectionRestricted
            ? GetFullScreenSelectionMembers()
            : Browse.VisibleImages;
        return (SelectedImage == null ? -1 : images.IndexOf(SelectedImage), images.Count);
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
        if (TryMoveWithinFullScreenSelection(-itemsPerRow)) return;
        MoveFocusAndSelection(Browse.MoveVisible(SelectedImage, -itemsPerRow));
    }

    /// <summary>
    /// Navigate down by the specified number of items (one row in grid view).
    /// </summary>
    public void SelectImageDown(int itemsPerRow)
    {
        if (TryMoveWithinFullScreenSelection(itemsPerRow)) return;
        MoveFocusAndSelection(Browse.MoveVisible(SelectedImage, itemsPerRow));
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
        if (IsDevelopMode || IsFullScreenMode) return;

        Browse.SelectOnly(image);
        UpdateSelectedCount();
    }
}

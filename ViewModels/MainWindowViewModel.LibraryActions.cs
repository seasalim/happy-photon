using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{    [RelayCommand]
    private async Task DeleteImageAsync()
    {
        if (IsFullScreenMode) return;

        if (SelectedImage == null) return;

        if (ConfirmMoveToTrashAsync != null)
        {
            var confirmed = await ConfirmMoveToTrashAsync(SelectedImage.FileName);
            if (!confirmed) return;
        }

        var imageToDelete = SelectedImage;
        var movedToTrash = await _fileOperationService.MoveToTrashAsync(imageToDelete.FilePath);
        if (!movedToTrash)
        {
            return;
        }

        if (imageToDelete.CatalogId != 0)
        {
            await _catalogService.DeleteImageAsync(imageToDelete.CatalogId);
            imageToDelete.CatalogId = 0;
        }

        var replacement = Library.ReplacementAfterRemoval(imageToDelete);
        SelectedImage = replacement;
        Library.Remove(imageToDelete);
        UpdateSelectedCount();
    }

    [RelayCommand]
    private async Task DeleteRejectedImagesAsync()
    {
        if (IsFullScreenMode) return;

        var rejectedImages = Library.GetRejectedImages().ToList();
        if (rejectedImages.Count == 0) return;

        if (ConfirmDeleteRejectedAsync != null)
        {
            var confirmed = await ConfirmDeleteRejectedAsync(rejectedImages.Count, CurrentFolderPath);
            if (!confirmed) return;
        }

        var selectedImage = SelectedImage;
        var replacement = selectedImage != null && rejectedImages.Contains(selectedImage)
            ? Library.ReplacementAfterRemoval(selectedImage)
            : selectedImage;
        var deletedImages = new List<ImageFile>();
        var failureCount = 0;

        foreach (var image in rejectedImages)
        {
            try
            {
                if (!await _fileOperationService.MoveToTrashAsync(image.FilePath))
                {
                    failureCount++;
                    continue;
                }

                if (image.CatalogId != 0)
                {
                    await _catalogService.DeleteImageAsync(image.CatalogId);
                    image.CatalogId = 0;
                }
                deletedImages.Add(image);
            }
            catch
            {
                failureCount++;
            }
        }

        if (deletedImages.Count > 0)
        {
            Library.RemoveRange(deletedImages);

            if (selectedImage != null && deletedImages.Contains(selectedImage))
            {
                SelectedImage = replacement != null && Library.ContainsVisible(replacement)
                    ? replacement
                    : Library.FirstVisible();
            }

            UpdateSelectedCount();
        }

        if (failureCount > 0 && ShowDeleteRejectedFailuresAsync != null)
        {
            await ShowDeleteRejectedFailuresAsync(failureCount);
        }
    }

    [RelayCommand]
    private void SelectPreviousImage()
    {
        var previous = Library.PreviousVisible(SelectedImage);
        if (previous != null) SelectedImage = previous;
    }

    [RelayCommand]
    private void SelectNextImage()
    {
        var next = Library.NextVisible(SelectedImage);
        if (next != null) SelectedImage = next;
    }

    /// <summary>
    /// Navigate up by the specified number of items (one row in grid view).
    /// </summary>
    public void SelectImageUp(int itemsPerRow)
    {
        var image = Library.MoveVisible(SelectedImage, -itemsPerRow);
        if (image != null) SelectedImage = image;
    }

    /// <summary>
    /// Navigate down by the specified number of items (one row in grid view).
    /// </summary>
    public void SelectImageDown(int itemsPerRow)
    {
        var image = Library.MoveVisible(SelectedImage, itemsPerRow);
        if (image != null) SelectedImage = image;
    }

    public void SelectFirstImage()
    {
        SelectedImage = Library.FirstVisible();
    }

    public void SelectLastImage()
    {
        SelectedImage = Library.LastVisible();
    }

    [RelayCommand]
    private async Task PickImageAsync()
    {
        if (IsFullScreenMode) return;

        await SetFlagStateAsync(ImageFlag.Picked);
    }

    [RelayCommand]
    private async Task TogglePickedImageAsync()
    {
        if (IsFullScreenMode) return;

        var flag = SelectedImage?.IsPicked == true
            ? ImageFlag.Unflagged
            : ImageFlag.Picked;
        await SetFlagStateAsync(flag);
    }

    [RelayCommand]
    private async Task UnpickImageAsync()
    {
        if (IsFullScreenMode) return;

        await SetFlagStateAsync(ImageFlag.Unflagged);
    }

    [RelayCommand]
    private async Task RejectImageAsync()
    {
        if (IsFullScreenMode) return;

        await SetFlagStateAsync(ImageFlag.Rejected);
    }

    [RelayCommand]
    private async Task ToggleRejectedImageAsync()
    {
        if (IsFullScreenMode) return;

        var flag = SelectedImage?.IsRejected == true
            ? ImageFlag.Unflagged
            : ImageFlag.Rejected;
        await SetFlagStateAsync(flag);
    }

    private async Task SetFlagStateAsync(ImageFlag flag)
    {
        if (SelectedImage == null) return;

        var image = SelectedImage;
        if (image.Flag == flag) return;

        var replacement = Library.MatchesCurrentFilters(image, flag) ? null : Library.ReplacementAfterRemoval(image);

        image.Flag = flag;
        await image.EnsureCatalogIdAsync(_catalogService);
        await _catalogService.SaveFlagStateAsync(image.CatalogId, image.Flag);

        Library.RefreshFilters();
        if (replacement != null && Library.ContainsVisible(replacement))
        {
            SelectedImage = replacement;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private async Task SetRatingAsync(int rating)
    {
        if (IsFullScreenMode) return;
        if (SelectedImage == null) return;

        rating = Math.Clamp(rating, 0, 5);
        var image = SelectedImage;
        if (image.Rating == rating) return;

        var replacement = Library.MatchesCurrentFilters(image, rating)
            ? null
            : Library.ReplacementAfterRemoval(image);

        image.Rating = rating;
        await image.EnsureCatalogIdAsync(_catalogService);
        await _catalogService.SaveRatingAsync(image.CatalogId, image.Rating);

        Library.RefreshFilters();
        if (replacement != null && Library.ContainsVisible(replacement))
        {
            SelectedImage = replacement;
        }
        UpdateSelectedCount();
    }
}

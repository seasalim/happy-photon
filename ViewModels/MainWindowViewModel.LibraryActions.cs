using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private async Task DeleteImageAsync()
    {
        if (IsFullScreenMode) return;

        var targets = ResolveActionTargets().Targets;
        if (targets.Count == 0 || ConfirmMoveToTrashAsync == null) return;

        var confirmed = await ConfirmMoveToTrashAsync(
            targets.Count,
            targets.Count == 1 ? targets[0].FileName : null);
        if (confirmed) await DeleteBatchAsync(targets);
    }

    [RelayCommand]
    private async Task DeleteRejectedImagesAsync()
    {
        if (IsFullScreenMode) return;

        var rejectedImages = Library.GetRejectedImages().ToList();
        if (rejectedImages.Count == 0) return;

        if (ConfirmDeleteRejectedAsync == null) return;
        var confirmed = await ConfirmDeleteRejectedAsync(
            rejectedImages.Count, CurrentFolderPath);
        if (confirmed) await DeleteBatchAsync(rejectedImages);
    }

    private async Task DeleteBatchAsync(IReadOnlyList<ImageFile> targets)
    {
        var claimedPaths = targets.Select(image => image.FilePath).ToArray();
        SetDeleteTargetsClaimed(claimedPaths, claimed: true);
        var deletedImages = new List<ImageFile>();
        var failures = new List<FileOperationFailure>();
        var selectedImage = SelectedImage;
        var replacement = selectedImage != null && targets.Contains(selectedImage)
            ? Library.ReplacementAfterRemoval(selectedImage)
            : selectedImage;
        var folderImagePaths = Library.AllImages
            .Select(image => image.FilePath)
            .ToArray();

        try
        {
            if (_xmpWriter != null) await _xmpWriter.DrainAsync();

            foreach (var image in targets)
            {
                await DeleteOneAsync(
                    image, folderImagePaths, deletedImages, failures);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Delete batch failed: {exception.Message}");
            foreach (var image in targets.Except(deletedImages))
            {
                failures.Add(new FileOperationFailure(
                    image.FilePath, "The delete operation could not be completed."));
            }
        }
        finally
        {
            SetDeleteTargetsClaimed(claimedPaths, claimed: false);
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

        if (failures.Count > 0 && ShowFileOperationFailuresAsync != null)
        {
            try
            {
                await ShowFileOperationFailuresAsync(failures);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Delete summary failed: {exception.Message}");
            }
        }
    }

    private async Task DeleteOneAsync(
        ImageFile image,
        IReadOnlyCollection<string> folderImagePaths,
        ICollection<ImageFile> deletedImages,
        ICollection<FileOperationFailure> failures)
    {
        try
        {
            if (_sourceAvailabilityService.GetAvailability(
                    image.FilePath).IsOnlineOnly())
            {
                failures.Add(new FileOperationFailure(
                    image.FilePath, "The file is online-only and was not downloaded."));
                return;
            }

            var pathAssessment = _fileOperationService.AssessTrashPath(
                image.FilePath);
            if (!pathAssessment.IsSupported)
            {
                failures.Add(new FileOperationFailure(
                    image.FilePath, pathAssessment.Reason ??
                    "The file cannot be moved to Trash safely."));
                return;
            }

            if (!await _fileOperationService.MoveToTrashAsync(image.FilePath))
            {
                failures.Add(new FileOperationFailure(
                    image.FilePath, "The file could not be moved to Trash."));
                return;
            }

            deletedImages.Add(image);
            await MoveResolvedSidecarsToTrashAsync(
                image, folderImagePaths, failures);

            if (image.CatalogId != 0)
            {
                try
                {
                    await _catalogService.DeleteImageAsync(image.CatalogId);
                    image.CatalogId = 0;
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Catalog cleanup failed: {exception.Message}");
                    failures.Add(new FileOperationFailure(image.FilePath,
                        "The file was moved to Trash, but its catalog entry was left behind."));
                }
            }
        }
        catch (Exception exception)
        {
            failures.Add(new FileOperationFailure(
                image.FilePath, exception.Message));
        }
    }

    private async Task MoveResolvedSidecarsToTrashAsync(
        ImageFile image,
        IReadOnlyCollection<string> folderImagePaths,
        ICollection<FileOperationFailure> failures)
    {
        XmpSidecarResolution resolution;
        try
        {
            resolution = XmpSidecarPaths.Resolve(
                image.FilePath,
                folderImagePaths,
                XmpSidecarNaming);
        }
        catch (Exception exception)
        {
            failures.Add(new FileOperationFailure(
                image.FilePath, $"The sidecar could not be resolved: {exception.Message}"));
            return;
        }

        foreach (var sidecar in new[] { resolution.Winner, resolution.Shadowed })
        {
            if (sidecar == null) continue;

            try
            {
                if (_sourceAvailabilityService.GetAvailability(
                        sidecar.Path).IsOnlineOnly())
                {
                    failures.Add(new FileOperationFailure(
                        sidecar.Path, "The sidecar is online-only and was not downloaded."));
                    continue;
                }

                var pathAssessment = _fileOperationService.AssessTrashPath(sidecar.Path);
                if (!pathAssessment.IsSupported)
                {
                    failures.Add(new FileOperationFailure(
                        sidecar.Path, pathAssessment.Reason ??
                        "The sidecar cannot be moved to Trash safely."));
                    continue;
                }

                if (!await _fileOperationService.MoveToTrashAsync(sidecar.Path))
                {
                    failures.Add(new FileOperationFailure(
                        sidecar.Path, "The sidecar could not be moved to Trash."));
                }
            }
            catch (Exception exception)
            {
                failures.Add(new FileOperationFailure(
                    sidecar.Path, exception.Message));
            }
        }
    }

    [RelayCommand]
    private void SelectPreviousImage()
    {
        if (TryMoveWithinFullScreenSelection(-1)) return;

        MoveFocusAndSelection(Library.PreviousVisible(SelectedImage));
    }

    [RelayCommand]
    private void SelectNextImage()
    {
        if (TryMoveWithinFullScreenSelection(1)) return;

        MoveFocusAndSelection(Library.NextVisible(SelectedImage));
    }

    /// <summary>
    /// Navigate up by the specified number of items (one row in grid view).
    /// </summary>
    public void SelectImageUp(int itemsPerRow)
    {
        if (TryMoveWithinFullScreenSelection(-itemsPerRow)) return;

        MoveFocusAndSelection(Library.MoveVisible(SelectedImage, -itemsPerRow));
    }

    /// <summary>
    /// Navigate down by the specified number of items (one row in grid view).
    /// </summary>
    public void SelectImageDown(int itemsPerRow)
    {
        if (TryMoveWithinFullScreenSelection(itemsPerRow)) return;

        MoveFocusAndSelection(Library.MoveVisible(SelectedImage, itemsPerRow));
    }

    public void SelectFirstImage()
    {
        if (TrySelectFullScreenSelectionBoundary(last: false)) return;

        SelectedImage = Library.FirstVisible();
        if (SelectedImage != null) MoveSelectionWithFocus(SelectedImage);
    }

    public void SelectLastImage()
    {
        if (TrySelectFullScreenSelectionBoundary(last: true)) return;

        SelectedImage = Library.LastVisible();
        if (SelectedImage != null) MoveSelectionWithFocus(SelectedImage);
    }

    private void MoveFocusAndSelection(ImageFile? image)
    {
        if (image == null) return;

        SelectedImage = image;
        MoveSelectionWithFocus(image);
    }

    // Keyboard navigation in the Library grid carries the selection with the
    // focused image so assessment actions land on the photo under the ring.
    private void MoveSelectionWithFocus(ImageFile image)
    {
        if (IsDevelopMode || IsFullScreenMode) return;

        Library.SelectOnly(image);
        UpdateSelectedCount();
    }

    [RelayCommand]
    private async Task TogglePickedImageAsync()
    {
        if (IsFullScreenMode) return;

        await SetFlagStateAsync(ImageFlag.Picked, toggleUniform: true);
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

        await SetFlagStateAsync(ImageFlag.Rejected, toggleUniform: true);
    }

    private async Task SetFlagStateAsync(
        ImageFlag flag,
        bool toggleUniform = false)
    {
        var targets = ResolveActionTargets().Targets;
        if (targets.Count == 0) return;
        var actedOnImage = targets.Count == 1 ? targets[0] : null;
        var previousFlag = actedOnImage?.Flag ?? ImageFlag.Unflagged;

        var next = toggleUniform && flag != ImageFlag.Unflagged &&
                   targets.All(image => image.Flag == flag)
            ? ImageFlag.Unflagged
            : flag;
        if (targets.All(image => image.Flag == next))
        {
            if (actedOnImage != null)
            {
                ShowAssessmentFeedback(
                    actedOnImage,
                    DescribeFlagFeedback(next, previousFlag));
            }
            return;
        }

        var selectedImage = SelectedImage;
        var replacement = selectedImage != null &&
                          targets.Contains(selectedImage) &&
                          !Library.MatchesCurrentFilters(selectedImage, next)
            ? Library.ReplacementAfterRemoval(selectedImage)
            : null;

        try
        {
            foreach (var target in targets)
            {
                await target.EnsureCatalogIdAsync(_catalogService);
            }

            await CommitAssessmentAsync(targets.Select(target =>
                new AssessmentMutation(
                    target.CatalogId, AssessmentAxes.Flag, Flag: next)).ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Flag update failed: {ex.Message}");
            ShowTransientStatus("Unable to update flags");
            return;
        }

        foreach (var target in targets)
        {
            target.Flag = next;
        }

        Library.RefreshFilters();
        if (replacement != null && Library.ContainsVisible(replacement))
        {
            SelectedImage = replacement;
        }
        UpdateSelectedCount();
        if (targets.Count > 1)
        {
            var action = next switch
            {
                ImageFlag.Picked => "Picked",
                ImageFlag.Rejected => "Rejected",
                _ => "Unflagged"
            };
            ShowTransientStatus($"{action} {targets.Count} photos");
        }
        else if (actedOnImage != null &&
                 ReferenceEquals(SelectedImage, actedOnImage))
        {
            ShowAssessmentFeedback(
                actedOnImage,
                DescribeFlagFeedback(next, previousFlag));
        }
    }

    private static string DescribeFlagFeedback(
        ImageFlag next,
        ImageFlag previous) =>
        next != ImageFlag.Unflagged
            ? $"Set flag: {next}"
            : previous != ImageFlag.Unflagged
                ? $"Unset flag: {previous}"
                : "Unset flag";

    [RelayCommand]
    private async Task SetRatingAsync(int rating)
    {
        if (IsFullScreenMode) return;

        rating = Math.Clamp(rating, 0, 5);
        var targets = ResolveActionTargets().Targets;
        if (targets.Count == 0) return;
        var actedOnImage = targets.Count == 1 ? targets[0] : null;
        var previousRating = actedOnImage?.Rating ?? 0;
        if (targets.All(image => image.Rating == rating))
        {
            if (actedOnImage != null)
            {
                ShowAssessmentFeedback(
                    actedOnImage,
                    DescribeRatingFeedback(rating, previousRating));
            }
            return;
        }

        var selectedImage = SelectedImage;
        var replacement = selectedImage != null &&
                          targets.Contains(selectedImage) &&
                          !Library.MatchesCurrentFilters(selectedImage, rating)
            ? Library.ReplacementAfterRemoval(selectedImage)
            : null;

        try
        {
            foreach (var target in targets)
            {
                await target.EnsureCatalogIdAsync(_catalogService);
            }

            await CommitAssessmentAsync(targets.Select(target =>
                new AssessmentMutation(
                    target.CatalogId, AssessmentAxes.Rating,
                    Rating: rating)).ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Rating update failed: {ex.Message}");
            ShowTransientStatus("Unable to update ratings");
            return;
        }

        foreach (var target in targets)
        {
            target.Rating = rating;
        }

        Library.RefreshFilters();
        if (replacement != null && Library.ContainsVisible(replacement))
        {
            SelectedImage = replacement;
        }
        UpdateSelectedCount();
        if (targets.Count > 1)
        {
            ShowTransientStatus($"Rated {targets.Count} photos");
        }
        else if (actedOnImage != null &&
                 ReferenceEquals(SelectedImage, actedOnImage))
        {
            ShowAssessmentFeedback(
                actedOnImage,
                DescribeRatingFeedback(rating, previousRating));
        }
    }

    private static string DescribeRatingFeedback(int next, int previous) =>
        next > 0
            ? $"Set rating: {new string('★', next)}"
            : previous > 0
                ? $"Unset rating: {new string('★', previous)}"
                : "Unset rating";
}

using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

internal sealed record DeleteConfirmationRequest(
    IReadOnlyList<ImageFile> Versions, IReadOnlyList<ImageFile> Primaries);

public partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanDeleteImage))]
    private async Task DeleteImageAsync()
    {
        if (IsSpotsMode)
        {
            await DeleteSpotAsync();
            return;
        }
        if (IsFullScreenMode || IsCompareMode) return;

        var targets = ResolveActionTargets().Targets;
        if (targets.Count == 0) return;
        await ConfirmAndDeleteAsync(targets);
    }

    private bool CanDeleteImage() => !IsSpotsMode || IsEnterTextInputFocused?.Invoke() != true;

    private async Task ConfirmAndDeleteAsync(IReadOnlyList<ImageFile> targets)
    {
        var request = PartitionDeleteTargets(targets);
        if (ConfirmDeleteAsync == null ||
            !await ConfirmDeleteAsync(request)) return;

        await DeleteConfirmedTargetsAsync(request);
    }

    [RelayCommand]
    private async Task DeleteRejectedImagesAsync()
    {
        if (IsFullScreenMode || IsCompareMode) return;

        var rejectedImages = Browse.GetRejectedImages().ToList();
        if (rejectedImages.Count == 0) return;

        var request = PartitionDeleteTargets(rejectedImages);
        if (ConfirmDeleteRejectedAsync == null) return;
        var confirmed = await ConfirmDeleteRejectedAsync(
            request.Versions.Count,
            request.Primaries.Count,
            CurrentFolderPath);
        if (confirmed) await DeleteConfirmedTargetsAsync(request);
    }

    private static DeleteConfirmationRequest PartitionDeleteTargets(
        IReadOnlyList<ImageFile> targets)
    {
        var primaries = targets.Where(image => image.Version <= 1).ToList();
        var primaryPaths = primaries.Select(image => image.FilePath).ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var versions = targets.Where(image => image.Version > 1 &&
            !primaryPaths.Contains(image.FilePath)).ToList();
        return new DeleteConfirmationRequest(versions, primaries);
    }

    private async Task DeleteConfirmedTargetsAsync(DeleteConfirmationRequest request)
    {
        if (request.Versions.Count > 0)
            await DeleteVersionsBatchAsync(request.Versions);
        if (request.Primaries.Count > 0)
            await DeleteBatchAsync(request.Primaries);
    }

    private async Task DeleteBatchAsync(IReadOnlyList<ImageFile> targets)
    {
        var fileTargets = targets.GroupBy(image => image.FilePath,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToList();
        var claimedPaths = fileTargets.Select(image => image.FilePath).ToArray();
        SetDeleteTargetsClaimed(claimedPaths, claimed: true);
        var deletedImages = new List<ImageFile>();
        var failures = new List<FileOperationFailure>();
        var selectedImage = SelectedImage;
        var claimedPathSet = claimedPaths.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var replacement = selectedImage != null &&
                          claimedPathSet.Contains(selectedImage.FilePath)
            ? Browse.ReplacementAfterRemoval(selectedImage,
                candidate => !claimedPathSet.Contains(candidate.FilePath))
            : selectedImage;
        var folderImagePaths = Browse.AllImages
            .Select(image => image.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        try
        {
            if (_xmpWriter != null) await _xmpWriter.DrainAsync();

            foreach (var image in fileTargets)
            {
                await DeleteOneAsync(
                    image, folderImagePaths, deletedImages, failures);
            }
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Delete batch failed: {exception.Message}");
            foreach (var image in fileTargets.Where(image =>
                         !deletedImages.Any(deleted => string.Equals(
                             deleted.FilePath, image.FilePath,
                             StringComparison.OrdinalIgnoreCase))))
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
            Browse.RemoveRange(deletedImages);
            RefreshCapturePairsAfterRemoval();
            if (selectedImage != null && deletedImages.Contains(selectedImage))
            {
                SelectedImage = replacement != null && Browse.ContainsVisible(replacement)
                    ? replacement
                    : Browse.FirstVisible();
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
            var failure = await TryMoveToTrashAsync(image.FilePath, "file");
            if (failure != null)
            {
                failures.Add(new FileOperationFailure(image.FilePath, failure));
                return;
            }

            foreach (var sibling in Browse.AllImages.Where(candidate =>
                         string.Equals(candidate.FilePath, image.FilePath,
                             StringComparison.OrdinalIgnoreCase)))
                deletedImages.Add(sibling);
            await MoveResolvedSidecarsToTrashAsync(
                image, folderImagePaths, failures);

            if (image.CatalogId != 0)
            {
                try
                {
                    await _catalogService.DeleteFileAsync(image.FilePath);
                    foreach (var sibling in deletedImages.Where(candidate =>
                                 string.Equals(candidate.FilePath, image.FilePath,
                                     StringComparison.OrdinalIgnoreCase)))
                        sibling.CatalogId = 0;
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
                var failure = await TryMoveToTrashAsync(sidecar.Path, "sidecar");
                if (failure != null)
                {
                    failures.Add(new FileOperationFailure(sidecar.Path, failure));
                }
            }
            catch (Exception exception)
            {
                failures.Add(new FileOperationFailure(
                    sidecar.Path, exception.Message));
            }
        }
    }

    // Moves one path to Trash and returns why it was not moved, or null. The
    // noun ("file" or "sidecar") keeps each caller's failure messages.
    private async Task<string?> TryMoveToTrashAsync(string path, string noun)
    {
        if (_sourceAvailabilityService.GetAvailability(path).IsOnlineOnly())
        {
            return $"The {noun} is online-only and was not downloaded.";
        }

        var pathAssessment = _fileOperationService.AssessTrashPath(path);
        if (!pathAssessment.IsSupported)
        {
            return pathAssessment.Reason ?? $"The {noun} cannot be moved to Trash safely.";
        }

        return await _fileOperationService.MoveToTrashAsync(path)
            ? null
            : $"The {noun} could not be moved to Trash.";
    }

    [RelayCommand]
    private Task TogglePickedImageAsync() =>
        SetFlagStateAsync(ImageFlag.Picked);

    [RelayCommand]
    private Task UnpickImageAsync() =>
        SetFlagStateAsync(ImageFlag.Unflagged);

    [RelayCommand]
    private Task ToggleRejectedImageAsync() =>
        SetFlagStateAsync(ImageFlag.Rejected);

    [RelayCommand]
    private Task ToggleFlagAsync() =>
        SetFlagStateAsync(ImageFlag.Picked, toggleUniform: true);

    private Task SetFlagStateAsync(
        ImageFlag flag,
        bool toggleUniform = false) =>
        SetAssessmentAsync(FlagAxis, flag, toggleUniform);

    private static string DescribeFlagFeedback(
        ImageFlag next,
        ImageFlag previous) =>
        next != ImageFlag.Unflagged
            ? $"Set flag: {next}"
            : previous != ImageFlag.Unflagged
                ? $"Unset flag: {previous}"
                : "Unset flag";

    [RelayCommand]
    private Task SetRatingAsync(int rating) =>
        SetAssessmentAsync(RatingAxis, Math.Clamp(rating, 0, 5), toggles: true);

    private static string DescribeRatingFeedback(int next, int previous) =>
        next > 0
            ? $"Set rating: {new string('★', next)}"
            : previous > 0
                ? $"Unset rating: {new string('★', previous)}"
                : "Unset rating";
}

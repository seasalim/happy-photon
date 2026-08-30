using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private readonly Func<long, Task<bool>> _deleteCatalogVersionAsync;

    public Func<ImageFile, Task<string?>>? RequestVersionLabelAsync { get; set; }

    private bool CanCreateVersion(ImageFile? requested) =>
        !IsFullScreenMode && !IsCompareMode && IsBrowseOrDevelopMode &&
        (requested ?? SelectedImage) is { CanCreateVersion: true };

    [RelayCommand(CanExecute = nameof(CanCreateVersion))]
    private async Task NewVersionFromCurrentAsync(ImageFile? requested)
    {
        var source = requested ?? SelectedImage;
        if (!CanCreateVersion(source) || source == null) return;
        try
        {
            if (ReferenceEquals(source, SelectedImage) && IsDevelopMode)
            {
                SaveSlidersTo(source.EditSettings);
                source.HasEdits = source.EditSettings.HasEdits;
                await SaveEditSettingsAsync(source);
            }
            var state = await _catalogService.CreateVersionAsync(source.CatalogId);
            if (state == null) return;
            var sibling = new ImageFile(
                source.FilePath, source.SourceAvailabilityHint);
            ApplyCatalogState(sibling, state, source.VersionCount + 1);
            sibling.CopyMetadataFrom(source);
            ApplyCapturePairIndicator(sibling);
            Browse.InsertVersion(sibling);
            ApplyBurstIndicator(sibling);
            UpdateVersionCounts(source.FilePath);
            Browse.SelectOnly(sibling);
            SelectedImage = sibling;
            UpdateSelectedCount();
            _ = TrackDirectThumbnailOperation(RefreshThumbnailAsync(sibling));
            if (IsDevelopMode)
                ShowAssessmentFeedback(sibling, $"Version {sibling.Version}");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Create version failed: {exception.Message}");
            ShowTransientStatus("Unable to create version");
        }
        NotifyVersionCommandState();
    }

    private bool CanDeleteVersion(ImageFile? requested) =>
        !IsFullScreenMode && !IsCompareMode && IsBrowseMode &&
        (requested ?? SelectedImage) is { CanDeleteVersion: true };

    [RelayCommand(CanExecute = nameof(CanDeleteVersion))]
    private async Task DeleteVersionAsync(ImageFile? requested)
    {
        var image = requested ?? SelectedImage;
        if (!CanDeleteVersion(image) || image == null) return;
        await ConfirmAndDeleteAsync([image]);
    }

    private async Task DeleteVersionsBatchAsync(IReadOnlyList<ImageFile> targets)
    {
        var selectedImage = SelectedImage;
        var deletedVersions = new List<ImageFile>();
        var failures = new List<FileOperationFailure>();
        foreach (var image in targets)
        {
            try
            {
                if (await _deleteCatalogVersionAsync(image.CatalogId))
                {
                    deletedVersions.Add(image);
                }
                else
                {
                    failures.Add(new FileOperationFailure(
                        image.FilePath,
                        $"{image.VersionDisplayLabel} could not be deleted."));
                }
            }
            catch (Exception exception)
            {
                failures.Add(new FileOperationFailure(
                    image.FilePath,
                    $"{image.VersionDisplayLabel} could not be deleted: " +
                    exception.Message));
            }
        }

        if (deletedVersions.Count > 0)
        {
            var replacement = selectedImage == null ? null :
                Browse.ReplacementAfterRemoval(selectedImage,
                    candidate => !deletedVersions.Contains(candidate));
            Browse.RemoveRange(deletedVersions);
            RefreshCapturePairsAfterRemoval();
            foreach (var path in deletedVersions.Select(image => image.FilePath)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
                UpdateVersionCounts(path);
            if (selectedImage != null && deletedVersions.Contains(selectedImage))
                SelectedImage = replacement ?? Browse.FirstVisible();
            UpdateSelectedCount();
            NotifyVersionCommandState();
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

    [RelayCommand]
    private async Task RenameVersionLabelAsync(ImageFile? requested)
    {
        var image = requested ?? SelectedImage;
        if (image == null || !IsBrowseMode || IsCompareMode ||
            RequestVersionLabelAsync == null) return;
        var label = await RequestVersionLabelAsync(image);
        if (label == null) return;
        try
        {
            await _catalogService.RenameVersionAsync(image.CatalogId, label);
            image.VersionLabel = string.IsNullOrWhiteSpace(label)
                ? null
                : label.Trim();
            if (ReferenceEquals(image, SelectedImage))
                OnPropertyChanged(nameof(ActiveFileName));
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Rename version failed: {exception.Message}");
            ShowTransientStatus("Unable to rename version");
        }
    }

    private static void ApplyCatalogState(
        ImageFile image,
        CatalogImageState state,
        int versionCount)
    {
        image.CatalogId = state.CatalogId;
        image.Version = state.Version;
        image.VersionLabel = state.VersionLabel;
        image.VersionCount = versionCount;
        image.EditSettings = state.EditSettings;
        image.HasEdits = state.EditSettings.HasEdits;
        image.Flag = state.Flag;
        image.Rating = state.Rating;
        image.ColorLabel = state.ColorLabel;
        image.AssessmentRevision = state.AssessmentRevision;
        image.AssessedUtc = state.AssessedUtc;
        image.PendingAssessmentAxes = state.PendingAxes;
    }

    private void UpdateVersionCounts(string path)
    {
        var siblings = Browse.AllImages.Where(image =>
            string.Equals(image.FilePath, path,
                StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var sibling in siblings) sibling.VersionCount = siblings.Count;
    }

    private void NotifyVersionCommandState()
    {
        NewVersionFromCurrentCommand.NotifyCanExecuteChanged();
        DeleteVersionCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCompareModeChanged(bool value) =>
        NotifyVersionCommandState();
}

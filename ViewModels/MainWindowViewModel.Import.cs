using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public sealed record FirstRunImportCompletion(
    string BrowsingRootPath,
    string InitiallySelectedFolderPath,
    string? Message);

public partial class MainWindowViewModel
{
    public Func<Task>? RequestCatalogImportAsync { get; set; }
    public Func<string?>? CaptureLibraryViewportAnchor { get; set; }
    public Action<string?>? RestoreLibraryViewportAnchor { get; set; }
    public Func<FirstRunImportCompletion, Task>?
        PersistImportedFirstRunCompletionAsync { get; set; }

    [RelayCommand]
    private Task ImportCatalog() =>
        RequestCatalogImportAsync?.Invoke() ?? Task.CompletedTask;

    public async Task<LightroomCatalogContents> ReadLightroomCatalogAsync(
        string catalogPath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Lightroom catalog import is available on Windows in Phase 1. " +
                "macOS and Linux will be enabled after the snapshot safety suite is verified there.");
        }

        return await new LightroomCatalogReader().ReadAsync(
            catalogPath, _colorLabelNames, cancellationToken);
    }

    public Task<CatalogImportStoredSettings?> LoadCatalogImportSettingsAsync(
        string catalogPath) =>
        new CatalogImportService(_catalogService).LoadSettingsAsync(catalogPath);

    public Task<CatalogImportPreview> PreviewCatalogImportAsync(
        LightroomCatalogContents source,
        IReadOnlyDictionary<string, string> rootMappings,
        CatalogImportPolicy policy,
        CancellationToken cancellationToken = default) =>
        new CatalogImportService(_catalogService).CreatePreviewAsync(
            source, rootMappings, policy, cancellationToken);

    public async Task<CatalogImportApplyResult> ApplyCatalogImportAsync(
        CatalogImportPreview preview,
        CancellationToken cancellationToken = default)
    {
        var viewportAnchor = CaptureLibraryViewportAnchor?.Invoke();
        var selectedPaths = Library.AllImages
            .Where(image => image.IsSelected)
            .Select(image => image.FilePath)
            .ToHashSet(PathComparison);
        var selectedPath = SelectedImage?.FilePath;
        var oldVisibleIndex = SelectedImage == null
            ? -1
            : Library.VisibleImages.IndexOf(SelectedImage);

        var result = await new CatalogImportService(_catalogService)
            .ApplyAsync(preview, cancellationToken);
        AdoptImportedAssessments(result.Adoptions);
        if (result.Adoptions.Count > 0)
        {
            Library.RefreshFilters();
            foreach (var image in Library.VisibleImages)
                image.IsSelected = selectedPaths.Contains(image.FilePath);
            SelectedImage = Library.VisibleImages.FirstOrDefault(image =>
                    string.Equals(image.FilePath, selectedPath, PathStringComparison))
                ?? VisibleNearIndex(oldVisibleIndex);
            UpdateSelectedCount();
            RestoreLibraryViewportAnchor?.Invoke(viewportAnchor);
        }

        if (IsFirstRunVisible)
            await CompleteFirstRunAfterImportAsync(preview, result.Report);
        return result;
    }

    internal void AdoptImportedAssessments(
        IReadOnlyList<CatalogImportAdoption> adoptions)
    {
        var liveById = Library.AllImages
            .Where(image => image.CatalogId != 0)
            .ToDictionary(image => image.CatalogId);
        foreach (var adoption in adoptions)
        {
            if (!liveById.TryGetValue(adoption.Snapshot.ImageId, out var image) ||
                image.AssessmentRevision != adoption.BaselineRevision)
            {
                continue;
            }

            var snapshot = adoption.Snapshot;
            image.Flag = snapshot.Flag;
            image.Rating = snapshot.Rating;
            image.ColorLabel = snapshot.ColorLabel;
            image.AssessmentRevision = snapshot.Revision;
            image.AssessedUtc = snapshot.AssessedUtc;
            image.PendingAssessmentAxes = snapshot.PendingAxes;
        }
    }

    private ImageFile? VisibleNearIndex(int oldIndex)
    {
        if (Library.VisibleImages.Count == 0) return null;
        return Library.VisibleImages[Math.Clamp(oldIndex, 0,
            Library.VisibleImages.Count - 1)];
    }

    private async Task CompleteFirstRunAfterImportAsync(
        CatalogImportPreview preview,
        CatalogImportReport report)
    {
        var completion = await Task.Run(() =>
            FindFirstRunImportCompletion(preview, report));
        if (completion == null || PersistImportedFirstRunCompletionAsync == null)
        {
            SetFirstRunError(
                "The metadata was imported, but Happy Photon could not save a browsing location. Choose a photo location to continue.");
            return;
        }

        await PersistImportedFirstRunCompletionAsync(completion);
        await InitializeFolderTreeWithRootAsync(
            completion.BrowsingRootPath,
            completion.InitiallySelectedFolderPath,
            selectFolder: false);
        var root = RootFolders.Single();
        var selected = NavigateToFolder(root, completion.InitiallySelectedFolderPath);
        await LoadFolderAsync(selected.Path);
        _suppressSelectedFolderLoad = true;
        try
        {
            SelectedFolder = selected;
        }
        finally
        {
            _suppressSelectedFolderLoad = false;
        }
        ShowWorkspaceReady(CurrentFirstRunExperienceVersion);
        if (!string.IsNullOrWhiteSpace(completion.Message))
            ShowTransientStatus(completion.Message);
        RequestFolderTreeFocus?.Invoke();
    }

    internal FirstRunImportCompletion? FindFirstRunImportCompletion(
        CatalogImportPreview preview,
        CatalogImportReport report)
    {
        var mappedRoots = preview.RootMappings.Values
            .Where(Directory.Exists)
            .Distinct(PathComparison)
            .ToArray();
        var pathsByFolder = preview.ImportedPaths
            .GroupBy(path => Path.GetDirectoryName(path) ?? path, PathComparison)
            .OrderByDescending(group => group.Count())
            .Take(10);
        foreach (var group in pathsByFolder)
        {
            if (!Directory.Exists(group.Key)) continue;
            var imported = group.ToHashSet(PathComparison);
            var intersection = _folderService.GetImagesInFolder(group.Key)
                .Count(image => imported.Contains(Path.GetFullPath(image.FilePath)));
            if (intersection == 0) continue;
            var root = mappedRoots
                .Where(candidate => IsWithin(candidate, group.Key))
                .OrderByDescending(candidate => candidate.Length)
                .FirstOrDefault() ?? group.Key;
            return new FirstRunImportCompletion(root, group.Key, null);
        }

        var fallback = mappedRoots.FirstOrDefault()
            ?? GetAvailablePicturesPath();
        if (fallback == null) return null;
        var message = report.NothingToImport
            ? "This Lightroom catalog has no ratings, picks, rejects, or color labels to import. Choose a folder to begin."
            : report.NothingMatched
                ? "None of the Lightroom photos with ratings, flags, or color labels matched. Review the location mappings, or choose a folder to continue."
                : $"Imported metadata for {report.MatchedPhotos} photos. Happy Photon couldn't automatically find a folder with them on this computer — the source may be offline.";
        return new FirstRunImportCompletion(
            fallback, fallback,
            message);
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal) &&
               !Path.IsPathFullyQualified(relative);
    }

    private static StringComparer PathComparison => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathStringComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}

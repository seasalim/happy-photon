using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public Func<Task>? RequestCatalogImportAsync { get; set; }
    public Func<string?>? CaptureLibraryViewportAnchor { get; set; }
    public Action<string?>? RestoreLibraryViewportAnchor { get; set; }

    public async Task<LightroomCatalogContents> ReadLightroomCatalogAsync(
        string catalogPath,
        CancellationToken cancellationToken = default)
    {
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

    private static StringComparer PathComparison => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathStringComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}

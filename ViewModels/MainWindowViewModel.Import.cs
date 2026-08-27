using CommunityToolkit.Mvvm.Input;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    public Func<Task>? RequestCatalogImportAsync { get; set; }
    public Func<string?>? CaptureBrowseViewportAnchor { get; set; }
    public Action<string?>? RestoreBrowseViewportAnchor { get; set; }

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
        var viewportAnchor = CaptureBrowseViewportAnchor?.Invoke();
        var selectedIds = Browse.AllImages
            .Where(image => image.IsSelected)
            .Select(image => image.CatalogId)
            .ToHashSet();
        var selectedId = SelectedImage?.CatalogId;
        var oldVisibleIndex = SelectedImage == null
            ? -1
            : Browse.VisibleImages.IndexOf(SelectedImage);

        var result = await new CatalogImportService(_catalogService)
            .ApplyAsync(preview, cancellationToken);
        AdoptImportedAssessments(result.Adoptions);
        if (result.Adoptions.Count > 0)
        {
            Browse.RefreshFilters();
            foreach (var image in Browse.VisibleImages)
                image.IsSelected = selectedIds.Contains(image.CatalogId);
            SelectedImage = Browse.VisibleImages.FirstOrDefault(image =>
                    image.CatalogId == selectedId)
                ?? VisibleNearIndex(oldVisibleIndex);
            UpdateSelectedCount();
            RestoreBrowseViewportAnchor?.Invoke(viewportAnchor);
        }

        return result;
    }

    internal void AdoptImportedAssessments(
        IReadOnlyList<CatalogImportAdoption> adoptions)
    {
        var liveById = Browse.AllImages
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
        if (Browse.VisibleImages.Count == 0) return null;
        return Browse.VisibleImages[Math.Clamp(oldIndex, 0,
            Browse.VisibleImages.Count - 1)];
    }

    private static StringComparer PathComparison => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static StringComparison PathStringComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}

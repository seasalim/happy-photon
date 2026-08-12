using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed record XmpReconcileResult(
    IReadOnlyList<XmpReconcileAdoption> Adoptions,
    IReadOnlyList<string> Reports);

public sealed class XmpSidecarReconciler
{
    private const int BatchSize = 100;
    private readonly CatalogService _catalog;
    private readonly XmpSidecarReader _reader;

    public XmpSidecarReconciler(CatalogService catalog)
        : this(catalog, new XmpSidecarReader())
    {
    }

    public XmpSidecarReconciler(
        CatalogService catalog,
        XmpSidecarReader reader)
    {
        _catalog = catalog;
        _reader = reader;
    }

    public async Task<XmpReconcileResult> ReconcileAsync(
        IReadOnlyCollection<string> folderImagePaths,
        IReadOnlyDictionary<ColorLabel, string> labelNames,
        XmpSidecarNaming naming,
        IReadOnlyCollection<string>? indexedSidecarPaths = null,
        CancellationToken cancellationToken = default)
    {
        var reports = new List<string>();
        var paths = folderImagePaths.ToArray();
        var states = await _catalog.LoadImageStatesAsync(paths, cancellationToken);
        var snapshots = await _catalog.LoadAssessmentSnapshotsAsync(
            states.Values.Select(state => state.CatalogId).ToArray(),
            cancellationToken);
        var byId = snapshots.ToDictionary(snapshot => snapshot.ImageId);
        var pending = new List<XmpReconcileItem>();
        var adopted = new List<XmpReconcileAdoption>();

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolution = XmpSidecarPaths.Resolve(
                path, paths, naming, indexedSidecarPaths);
            var baseSidecar = Path.ChangeExtension(path, ".xmp");
            if (resolution.BaseNameAmbiguous &&
                (indexedSidecarPaths?.Contains(
                    baseSidecar, StringComparer.OrdinalIgnoreCase) ??
                 File.Exists(baseSidecar)))
                reports.Add($"Ambiguous base-name XMP sidecar ignored for {path}");
            if (resolution.Shadowed != null)
                reports.Add($"Shadowed XMP sidecar ignored: {resolution.Shadowed.Path}");
            if (resolution.Winner == null)
            {
                continue;
            }
            AssessmentSnapshot snapshot;
            if (states.TryGetValue(path, out var state) &&
                byId.TryGetValue(state.CatalogId, out var existing))
            {
                snapshot = existing;
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imageId = await _catalog.GetOrCreateImageAsync(path);
                snapshot = (await _catalog.LoadAssessmentSnapshotsAsync(
                    [imageId], cancellationToken)).Single();
            }
            try
            {
                var facts = await _reader.ReadAsync(
                    resolution.Winner, labelNames, cancellationToken);
                if (facts != null)
                    pending.Add(new XmpReconcileItem(snapshot, resolution.Winner, facts));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                reports.Add($"Could not read XMP sidecar {resolution.Winner.Path}: {exception.Message}");
            }

            if (pending.Count >= BatchSize)
            {
                adopted.AddRange(await _catalog.AdoptSidecarFactsAsync(
                    pending, cancellationToken));
                pending.Clear();
            }
        }
        if (pending.Count > 0)
            adopted.AddRange(await _catalog.AdoptSidecarFactsAsync(pending, cancellationToken));
        return new XmpReconcileResult(adopted, reports);
    }
}

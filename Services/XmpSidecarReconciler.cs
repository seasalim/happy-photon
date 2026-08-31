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
    private readonly ISourceAvailabilityService _availability;
    private readonly TryReadExifOrientation _readOrientation;

    public XmpSidecarReconciler(CatalogService catalog)
        : this(catalog, new XmpSidecarReader(),
            new SourceAvailabilityService(),
            ImageServiceHelpers.TryGetExifOrientation)
    {
    }

    public XmpSidecarReconciler(
        CatalogService catalog,
        XmpSidecarReader reader)
        : this(catalog, reader, new SourceAvailabilityService(),
            ImageServiceHelpers.TryGetExifOrientation)
    {
    }

    internal XmpSidecarReconciler(
        CatalogService catalog,
        XmpSidecarReader reader,
        ISourceAvailabilityService availability,
        TryReadExifOrientation readOrientation)
    {
        _catalog = catalog;
        _reader = reader;
        _availability = availability;
        _readOrientation = readOrientation;
    }

    public async Task<XmpReconcileResult> ReconcileAsync(
        IReadOnlyCollection<string> folderImagePaths,
        IReadOnlyDictionary<ColorLabel, string> labelNames,
        XmpSidecarNaming naming,
        IReadOnlyCollection<string>? indexedSidecarPaths = null,
        CancellationToken cancellationToken = default)
    {
        var reports = new List<string>();
        var paths = folderImagePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var states = await _catalog.LoadImageStatesAsync(paths, cancellationToken);
        var snapshots = await _catalog.LoadAssessmentSnapshotsAsync(
            states.Values.SelectMany(versions => versions)
                .Where(state => state.Version == 1)
                .Select(state => state.CatalogId).ToArray(),
            cancellationToken);
        var byId = snapshots.ToDictionary(snapshot => snapshot.ImageId);
        var pending = new List<XmpReconcileItem>();
        var adopted = new List<XmpReconcileAdoption>();
        var unsupportedCrops = 0;
        string? unsupportedCropExample = null;

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
            EditSettings localSettings;
            if (states.TryGetValue(path, out var versions) &&
                versions.FirstOrDefault(state => state.Version == 1) is { } state &&
                byId.TryGetValue(state.CatalogId, out var existing))
            {
                snapshot = existing;
                localSettings = state.EditSettings;
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imageId = await _catalog.GetOrCreateImageAsync(path);
                snapshot = (await _catalog.LoadAssessmentSnapshotsAsync(
                    [imageId], cancellationToken)).Single();
                localSettings = new EditSettings();
            }
            try
            {
                var facts = await _reader.ReadAsync(
                    resolution.Winner, labelNames, cancellationToken);
                if (facts != null)
                {
                    if (facts.Crop.Kind == XmpFactKind.Matched)
                    {
                        if (snapshot.PendingAxes.HasFlag(AssessmentAxes.Crop) ||
                            XmpCropProjection.HasGeometryEdits(localSettings))
                        {
                            facts = facts with { Crop = XmpFact<CropRegion>.Missing };
                        }
                        else if (!SourceAccessPolicy.CanRead(
                                _availability.GetAvailability(path),
                                SourceReadIntent.Background))
                        {
                            facts = facts with { Crop = XmpFact<CropRegion>.Missing };
                        }
                        else if (!_readOrientation(path, out var orientation))
                        {
                            facts = facts with { Crop = XmpFact<CropRegion>.Missing };
                        }
                        else if (orientation is not (0 or 1))
                        {
                            facts = facts with { Crop = XmpFact<CropRegion>.Unsupported };
                        }
                    }
                    if (facts.Crop.Kind == XmpFactKind.Unsupported)
                    {
                        unsupportedCrops++;
                        unsupportedCropExample ??= path;
                    }
                    pending.Add(new XmpReconcileItem(snapshot, resolution.Winner, facts));
                }
            }
            catch (XmpSidecarTooLargeException exception)
            {
                reports.Add(exception.Message);
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
        if (unsupportedCrops > 0)
        {
            reports.Add(
                $"Unsupported XMP crops skipped: {unsupportedCrops}; example: {unsupportedCropExample}");
        }
        return new XmpReconcileResult(adopted, reports);
    }
}

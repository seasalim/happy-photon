using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class CatalogImportService
{
    private readonly CatalogService _catalogService;
    private readonly Func<string, bool> _fileExists;

    public CatalogImportService(CatalogService catalogService) =>
        (_catalogService, _fileExists) = (catalogService, File.Exists);

    internal CatalogImportService(
        CatalogService catalogService,
        Func<string, bool> fileExists) =>
        (_catalogService, _fileExists) = (catalogService, fileExists);

    public async Task<CatalogImportStoredSettings?> LoadSettingsAsync(
        string catalogPath)
    {
        var json = await _catalogService.GetAppSettingAsync(
            GetSettingsKey(catalogPath));
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<CatalogImportStoredSettings>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<CatalogImportPreview> CreatePreviewAsync(
        LightroomCatalogContents source,
        IReadOnlyDictionary<string, string> requestedMappings,
        CatalogImportPolicy policy,
        CancellationToken cancellationToken = default)
    {
        var mappings = ResolveMappings(source.Roots, requestedMappings);
        var normalized = new Dictionary<string, CatalogImportRecord>(
            PathComparer);
        var duplicateDestinations = new HashSet<string>(PathComparer);
        var duplicateRecords = 0;
        var unresolved = 0;
        var unsupportedFiles = 0;
        var virtualCopies = 0;

        foreach (var record in source.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.IsVirtualCopy)
            {
                virtualCopies++;
                continue;
            }
            if (!IsSupportedImage(record.RelativePath))
            {
                unsupportedFiles++;
                continue;
            }
            if (!mappings.TryGetValue(record.SourceRoot, out var mappedRoot))
            {
                unresolved++;
                continue;
            }

            var path = NormalizeMappedPathFromNormalizedRoot(
                mappedRoot, record.RelativePath);
            if (normalized.ContainsKey(path))
            {
                duplicateDestinations.Add(path);
                duplicateRecords++;
            }
            normalized[path] = record;
        }

        var states = await _catalogService.LoadImportBaselinesAsync(
            normalized.Keys.ToArray(), cancellationToken);
        var changes = new List<CatalogImportChange>(normalized.Count);
        var rating = new MutableAxisSummary();
        var flag = new MutableAxisSummary();
        var label = new MutableAxisSummary();
        var unsupportedLabels = new Dictionary<string, int>(StringComparer.Ordinal);
        var updatedPhotos = 0;
        var existingRows = 0;
        var unavailableFiles = 0;

        foreach (var (path, record) in normalized)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var baseline = states.TryGetValue(path, out var state)
                ? state
                : EmptyBaseline;
            var availablePath = baseline.FilePath ?? path;
            if (!_fileExists(availablePath))
            {
                unavailableFiles++;
                continue;
            }
            if (baseline.Exists) existingRows++;

            var axes = AssessmentAxes.None;
            int? nextRating = null;
            ImageFlag? nextFlag = null;
            ColorLabel? nextLabel = null;
            Evaluate(record.Rating, baseline.Rating, 0, policy, rating,
                AssessmentAxes.Rating, ref axes, ref nextRating);
            Evaluate(record.Flag, baseline.Flag, ImageFlag.Unflagged, policy, flag,
                AssessmentAxes.Flag, ref axes, ref nextFlag);
            Evaluate(record.ColorLabel, baseline.ColorLabel, ColorLabel.None, policy, label,
                AssessmentAxes.Label, ref axes, ref nextLabel);
            if (record.ColorLabel.Kind == CatalogImportFactKind.Unsupported &&
                !string.IsNullOrEmpty(record.ColorLabel.SourceToken))
            {
                unsupportedLabels[record.ColorLabel.SourceToken] =
                    unsupportedLabels.GetValueOrDefault(record.ColorLabel.SourceToken) + 1;
            }
            if (axes != AssessmentAxes.None) updatedPhotos++;
            changes.Add(new CatalogImportChange(
                availablePath, baseline, source.CarriedAxes, axes,
                nextFlag, nextRating, nextLabel));
        }

        var actionable = new List<string>();
        var informational = new List<string>();
        if (source.Records.Count > 0 && changes.Count == 0)
            actionable.Add(unavailableFiles > 0
                ? "None of the Lightroom photos with ratings, flags, or color labels exist at their mapped paths. Copy or mount the originals, review the location mappings, and try again."
                : "None of the Lightroom photos with ratings, flags, or color labels matched a supported photo path. Review the location mappings and try again.");
        if (source.Records.Count == 0)
            informational.Add("This catalog has no ratings, picks, rejects, or color labels to import.");
        if (unresolved > 0)
            informational.Add(unresolved == 1
                ? "1 photo under an unmapped Lightroom location was not imported."
                : $"{unresolved} photos under unmapped Lightroom locations were not imported.");
        if (unavailableFiles > 0)
            informational.Add(unavailableFiles == 1
                ? "1 mapped photo file was not found and was not imported."
                : $"{unavailableFiles} mapped photo files were not found and were not imported.");
        if (virtualCopies > 0)
            informational.Add($"{virtualCopies} virtual copies were skipped; only master photos are imported.");
        if (unsupportedFiles > 0)
            informational.Add($"{unsupportedFiles} unsupported file types were skipped.");
        if (duplicateRecords > 0)
        {
            var destinationText = duplicateDestinations.Count == 1
                ? "1 destination path"
                : $"{duplicateDestinations.Count} destination paths";
            var recordText = duplicateRecords == 1
                ? "1 additional Lightroom record"
                : $"{duplicateRecords} additional Lightroom records";
            informational.Add(
                $"{recordText} mapped to {destinationText} already used by another record. The later record was used.");
        }
        foreach (var warning in source.SchemaWarnings) informational.Add(warning);
        if (!source.IsVerifiedVersion)
            informational.Add($"Lightroom catalog major version {source.MajorVersion} is compatible but unverified.");
        if (unsupportedLabels.Count > 0)
        {
            informational.Add(
                $"{unsupportedLabels.Values.Sum()} photos use color labels Happy Photon cannot map. Their labels will be left unchanged.");
        }

        var report = new CatalogImportReport(
            source.Records.Count, changes.Count, updatedPhotos,
            existingRows, changes.Count - existingRows,
            unresolved, unavailableFiles, unsupportedFiles, virtualCopies,
            rating.Freeze(), flag.Freeze(), label.Freeze(), unsupportedLabels,
            actionable, informational, !source.IsVerifiedVersion);
        var settingsKey = GetSettingsKey(source.CatalogPath);
        var baselineSettings = await _catalogService.GetAppSettingAsync(settingsKey);
        var stored = new CatalogImportStoredSettings(
            Path.GetFullPath(source.CatalogPath), mappings,
            new Dictionary<string, CatalogImportPolicy>
            {
                ["rating"] = policy,
                ["flag"] = policy,
                ["colorLabel"] = policy
            });
        var settingsJson = JsonSerializer.Serialize(stored);
        return new CatalogImportPreview(
            source.CatalogPath, policy, mappings, changes, report,
            settingsKey, baselineSettings, settingsJson,
            changes.Select(change => change.FilePath).ToArray());
    }

    public Task<CatalogImportApplyResult> ApplyAsync(
        CatalogImportPreview preview,
        CancellationToken cancellationToken = default)
    {
        if (preview.Report.NothingToImport || preview.Report.NothingMatched)
            return Task.FromResult(new CatalogImportApplyResult(
                preview.Report, [], 0));
        return _catalogService.ApplyImportAsync(preview, cancellationToken);
    }

    public static IReadOnlyDictionary<string, string> ResolveMappings(
        IReadOnlyList<CatalogSourceRoot> roots,
        IReadOnlyDictionary<string, string> requestedMappings)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            if (requestedMappings.TryGetValue(root.SourcePath, out var requested) &&
                TryNormalizeRoot(requested, out var mapped))
            {
                result[root.SourcePath] = mapped;
            }
            else if (TryNormalizeRoot(root.SourcePath, out var automatic) &&
                     Directory.Exists(automatic))
            {
                result[root.SourcePath] = automatic;
            }
        }
        return result;
    }

    internal static string NormalizeMappedPath(string mappedRoot, string relativePath)
    {
        if (!TryNormalizeRoot(mappedRoot, out var root))
            throw new InvalidDataException($"The mapped root '{mappedRoot}' is not a valid local folder.");
        return NormalizeMappedPathFromNormalizedRoot(root, relativePath);
    }

    private static string NormalizeMappedPathFromNormalizedRoot(
        string root,
        string relativePath)
    {
        var nativeRelative = relativePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace(OperatingSystem.IsWindows() ? '/' : '\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        if (nativeRelative.Length == 0 ||
            nativeRelative.AsSpan().IndexOf(':') >= 0)
        {
            throw new InvalidDataException("A Lightroom photo path has an invalid relative remainder.");
        }
        var combined = string.Concat(
            Path.TrimEndingDirectorySeparator(root),
            Path.DirectorySeparatorChar,
            nativeRelative);
        var fullPath = Path.GetFullPath(combined);
        var rootPrefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, PathStringComparison))
        {
            throw new InvalidDataException("A Lightroom photo path escapes its mapped source root.");
        }
        return fullPath;
    }

    private static bool TryNormalizeRoot(string value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || IsForeignSyntax(value)) return false;
        try
        {
            normalized = Path.GetFullPath(value);
            return Path.IsPathFullyQualified(normalized);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsForeignSyntax(string path) =>
        OperatingSystem.IsWindows()
            ? path.StartsWith("/Volumes/", StringComparison.Ordinal) ||
              path.StartsWith("/home/", StringComparison.Ordinal)
            : path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':' ||
              path.StartsWith("\\\\", StringComparison.Ordinal);

    private static bool IsSupportedImage(string relativePath)
    {
        var separator = Math.Max(
            relativePath.LastIndexOf('/'),
            relativePath.LastIndexOf('\\'));
        var fileName = separator >= 0
            ? relativePath[(separator + 1)..]
            : relativePath;
        return ImageFile.SupportedExtensions.Contains(Path.GetExtension(fileName));
    }

    private static void Evaluate<T>(
        CatalogImportFact<T> source,
        T local,
        T empty,
        CatalogImportPolicy policy,
        MutableAxisSummary summary,
        AssessmentAxes axis,
        ref AssessmentAxes changedAxes,
        ref T? next)
        where T : struct
    {
        switch (source.Kind)
        {
            case CatalogImportFactKind.NotCarried:
                summary.NotImported++;
                return;
            case CatalogImportFactKind.Unsupported:
                summary.Unsupported++;
                return;
            case CatalogImportFactKind.Empty:
                if (EqualityComparer<T>.Default.Equals(local, empty)) summary.Unchanged++;
                else summary.PreservedByPolicy++;
                return;
        }

        if (EqualityComparer<T>.Default.Equals(local, source.Value))
        {
            summary.Unchanged++;
            return;
        }
        if (policy == CatalogImportPolicy.FillEmptyOnly &&
            !EqualityComparer<T>.Default.Equals(local, empty))
        {
            summary.PreservedByPolicy++;
            return;
        }
        summary.Written++;
        changedAxes |= axis;
        next = source.Value;
    }

    private static string GetSettingsKey(string path)
    {
        var canonical = Path.GetFullPath(path);
        var hash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return "lightroom_import_" + hash;
    }

    // The catalog's file_path identity is COLLATE NOCASE on every platform.
    private static StringComparer PathComparer => StringComparer.OrdinalIgnoreCase;

    private static StringComparison PathStringComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static readonly CatalogImportBaseline EmptyBaseline =
        new(false, 0, ImageFlag.Unflagged, 0, ColorLabel.None,
            0, null, AssessmentAxes.None, null);

    private sealed class MutableAxisSummary
    {
        public int Written;
        public int Unchanged;
        public int PreservedByPolicy;
        public int Unsupported;
        public int NotImported;

        public CatalogImportAxisSummary Freeze() =>
            new(Written, Unchanged, PreservedByPolicy, Unsupported, NotImported);
    }
}

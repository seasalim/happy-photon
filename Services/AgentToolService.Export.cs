using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed partial class AgentToolService
{
    public async Task<AgentExportResult> ExportImagesAsync(
        IReadOnlyList<string> ids,
        AgentExportOptions options)
    {
        ValidateBatch(ids, BatchCap);
        ValidateExportOptions(options);
        var format = AgentToolValidation.ParseExportFormat(options.Format);
        var outputColorSpace = AgentToolValidation.ParseOutputColorSpace(
            options.OutputColorSpace);
        var useSubfolders = options.Variants is { Count: > 0 };
        var variants = CreateExportVariants(options);
        if (ids.Count * variants.Count > 1000)
        {
            throw new AgentToolException(
                "Export is limited to 1000 image-variant outputs per call.");
        }

        var snapshot = await OnUiThreadAsync(() =>
        {
            if (string.IsNullOrWhiteSpace(_vm.CurrentFolderPath))
            {
                throw new AgentToolException("No folder is open.");
            }

            var (images, failed) = ResolveImages(ids);
            var root = Path.GetFullPath(_vm.CurrentFolderPath);
            var requestedOutput = options.OutputFolder ?? "export";
            var output = Path.GetFullPath(Path.IsPathFullyQualified(requestedOutput)
                ? requestedOutput
                : Path.Combine(root, requestedOutput));
            if (!IsSameOrDescendant(root, output))
            {
                throw new AgentToolException(
                    "Output folder must be inside the currently open folder.");
            }

            var settings = AgentExportSettingsFactory.Create(
                output,
                options,
                format,
                outputColorSpace,
                _vm.ExportSettings.StripLocationData,
                _vm.ExportSettings.OutputSharpening);
            var originalPaths = ExportSafety.BuildOriginalPathSet(
                _vm.Library.AllImages.Select(image => image.FilePath));
            return Task.FromResult((images, failed, settings, originalPaths));
        });

        try
        {
            Directory.CreateDirectory(snapshot.settings.OutputFolder);
        }
        catch (Exception ex)
        {
            throw new AgentToolException(
                $"Could not create the output folder: {SafeReason(ex)}");
        }

        return await ExportResolvedImagesAsync(
            snapshot.images,
            snapshot.failed,
            snapshot.settings,
            variants,
            useSubfolders,
            snapshot.originalPaths);
    }

    internal async Task<AgentExportResult> ExportResolvedImagesAsync(
        IReadOnlyList<ImageFile> images,
        List<AgentBatchFailure> failed,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        HashSet<string> originalPaths)
    {
        using var activity = _vm.BeginExportActivity(images.Count);
        var exported = new List<string>();
        var skipped = new List<string>();
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var targets = new HashSet<string>(comparer);

        var processed = 0;
        foreach (var image in images)
        {
            try
            {
                var pending = BuildPendingExports(
                    image,
                    settings,
                    variants,
                    useSubfolders,
                    originalPaths,
                    targets,
                    skipped,
                    failed);
                if (pending.Count == 0)
                {
                    continue;
                }

                var availability = _imageService.GetSourceAvailability(image);
                if (availability == SourceAvailability.RequiresHydration)
                {
                    AddFailures(
                        pending,
                        failed,
                        "source requires hydration",
                        "hydration_required");
                    continue;
                }

                if (availability == SourceAvailability.Unavailable)
                {
                    AddFailures(
                        pending,
                        failed,
                        "source is unavailable",
                        "source_unavailable");
                    continue;
                }

                await ExportOneAsync(
                    image,
                    settings,
                    useSubfolders,
                    pending,
                    exported,
                    failed);
            }
            finally
            {
                activity.Report(++processed);
            }
        }

        return new AgentExportResult(exported, skipped, failed);
    }

    private List<(ExportVariant Variant, string RelativePath)> BuildPendingExports(
        ImageFile image,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        HashSet<string> originalPaths,
        HashSet<string> targets,
        List<string> skipped,
        List<AgentBatchFailure> failed)
    {
        var pending = new List<(ExportVariant Variant, string RelativePath)>();
        foreach (var variant in variants)
        {
            var relativePath = useSubfolders
                ? $"{variant.Name}/{settings.GetOutputFileName(image.FileName)}"
                : settings.GetOutputFileName(image.FileName);
            try
            {
                var target = Path.GetFullPath(settings.GetOutputPath(
                    image.FileName,
                    variant,
                    useSubfolders));
                relativePath = ToOutputRelativePath(settings.OutputFolder, target);

                if (!IsSameOrDescendant(settings.OutputFolder, target))
                {
                    failed.Add(new AgentBatchFailure(
                        relativePath,
                        "output path escapes export folder"));
                }
                else if (ExportSafety.IsOriginalPath(target, originalPaths))
                {
                    failed.Add(new AgentBatchFailure(
                        relativePath,
                        "would overwrite an original image"));
                }
                else if (File.Exists(target))
                {
                    skipped.Add(relativePath);
                }
                else if (!targets.Add(target))
                {
                    failed.Add(new AgentBatchFailure(
                        relativePath,
                        "output name collision"));
                }
                else
                {
                    pending.Add((variant, relativePath));
                }
            }
            catch (Exception ex)
            {
                failed.Add(new AgentBatchFailure(
                    relativePath.Replace('\\', '/'),
                    SafeReason(ex)));
            }
        }

        return pending;
    }

    private async Task ExportOneAsync(
        ImageFile image,
        ExportSettings settings,
        bool useSubfolders,
        List<(ExportVariant Variant, string RelativePath)> pending,
        List<string> exported,
        List<AgentBatchFailure> failed)
    {
        try
        {
            var count = await _imageService.ExportBatchAsync(
                [image],
                settings,
                pending.Select(item => item.Variant).ToList(),
                useSubfolders,
                null,
                CancellationToken.None);
            if (count == 1)
            {
                exported.AddRange(pending.Select(item => item.RelativePath));
                return;
            }

            var requiresHydration = _imageService.GetSourceAvailability(image) ==
                SourceAvailability.RequiresHydration;
            AddFailures(
                pending,
                failed,
                requiresHydration
                    ? "source requires hydration"
                    : "source could not be read",
                requiresHydration ? "hydration_required" : null);
        }
        catch (Exception ex)
        {
            failed.AddRange(pending.Select(item =>
                CreateFailure(item.RelativePath, ex)));
        }
    }

    private static void AddFailures(
        IEnumerable<(ExportVariant Variant, string RelativePath)> pending,
        List<AgentBatchFailure> failed,
        string reason,
        string? code) =>
        failed.AddRange(pending.Select(item => new AgentBatchFailure(
            item.RelativePath,
            reason,
            code)));

    private static IReadOnlyList<ExportVariant> CreateExportVariants(
        AgentExportOptions options)
    {
        if (options.Variants is not { Count: > 0 })
        {
            ValidateMaxDimension(options.MaxDimension);
            return [new ExportVariant("export", options.MaxDimension)];
        }

        if (options.Variants.Count > 8)
        {
            throw new AgentToolException(
                "Variants must contain no more than 8 entries.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var variants = options.Variants.Select(item =>
        {
            var name = AgentToolValidation.SanitizeVariantName(item.Name);
            if (!names.Add(name))
            {
                throw new AgentToolException(
                    "Variant names must be unique after sanitization.");
            }

            ValidateMaxDimension(item.MaxDimension);
            return new ExportVariant(name, item.MaxDimension);
        });
        return variants
            .OrderBy(item => item.MaxDimension.HasValue ? 1 : 0)
            .ThenByDescending(item => item.MaxDimension ?? 0)
            .ToList();
    }

    private static void ValidateMaxDimension(int? value)
    {
        if (value is <= 0 or > 65536)
        {
            throw new AgentToolException(
                "Maximum dimension must be between 1 and 65536.");
        }
    }

    private static string ToOutputRelativePath(
        string outputFolder,
        string fullPath) =>
        Path.GetRelativePath(outputFolder, fullPath).Replace('\\', '/');
}

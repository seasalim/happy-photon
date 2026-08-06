using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;

namespace HappyPhoton.Services;

public sealed class AgentToolService
{
    private const int BatchCap = 500;
    private const int StatsBatchCap = 200;

    private readonly MainWindowViewModel _vm;
    private readonly ImageService _imageService;
    private readonly CatalogService _catalogService;
    private readonly ImageStatsService _statsService = new();

    public AgentToolService(
        MainWindowViewModel vm,
        ImageService imageService,
        CatalogService catalogService)
    {
        _vm = vm;
        _imageService = imageService;
        _catalogService = catalogService;
    }

    public Task<AgentLibraryState> GetLibraryStateAsync() => OnUiThreadAsync(() =>
        Task.FromResult(new AgentLibraryState(
            _vm.CurrentFolderPath,
            _vm.Library.TotalCount,
            _vm.Library.VisibleCount,
            new AgentFilterState(
                _vm.Library.FileTypeFilter.ToString().ToLowerInvariant(),
                _vm.Library.FlagFilter.ToString().ToLowerInvariant(),
                _vm.Library.MinimumRating),
            _vm.SelectedImage?.FilePath,
            _vm.BurstsComputed)));

    public Task<List<AgentImageSummary>> ListImagesAsync(ListImagesRequest request)
    {
        ValidateListRequest(request);
        return OnUiThreadAsync(async () =>
        {
            var images = _vm.Library.AllImages
                .Where(image => MatchesFileType(image, request.FileType))
                .Where(image => MatchesFlag(image, request.Flag))
                .Where(image => !request.MinRating.HasValue || image.Rating >= request.MinRating.Value)
                .Skip(request.Offset)
                .Take(request.Limit)
                .ToList();

            if (request.LoadMetadata)
            {
                foreach (var image in images)
                {
                    await _imageService.LoadMetadataAsync(image);
                }
            }

            return images.Select(MapImage).ToList();
        });
    }

    public async Task<AgentImageStatsResult> GetImageStatsAsync(IReadOnlyList<string> ids)
    {
        ValidateBatch(ids, StatsBatchCap);
        var snapshot = await OnUiThreadAsync(async () =>
        {
            var (images, failed) = ResolveImages(ids);
            var sources = new List<(string Id, string Path, byte[]? Data)>();
            foreach (var image in images)
            {
                try
                {
                    if (image.CatalogId == 0)
                    {
                        image.CatalogId = await _catalogService.GetOrCreateImageAsync(image.FilePath);
                    }

                    var thumbnailPath = _catalogService.GetThumbnailPath(image.CatalogId);
                    byte[]? thumbnailData = null;
                    if (!_imageService.IsThumbnailCacheValid(image))
                    {
                        using var thumbnail = await _imageService.LoadUneditedThumbnailAsync(
                            image, CancellationToken.None);
                        if (thumbnail == null)
                        {
                            throw new AgentToolException("Thumbnail could not be generated.");
                        }

                        thumbnailData = BitmapConversionService.CreateEncodedSnapshot(thumbnail);
                    }

                    sources.Add((image.FilePath, thumbnailPath, thumbnailData));
                }
                catch (Exception ex)
                {
                    failed.Add(new AgentBatchFailure(image.FilePath, SafeReason(ex)));
                }
            }

            return (sources, failed);
        });

        var stats = new List<AgentImageStats>(snapshot.sources.Count);
        foreach (var item in snapshot.sources)
        {
            try
            {
                var result = item.Data != null
                    ? _statsService.Compute(item.Data)
                    : _statsService.Compute(item.Path);
                stats.Add(new AgentImageStats(
                    item.Id,
                    result.Sharpness,
                    result.ClippedHighlightsPct,
                    result.ClippedShadowsPct,
                    result.MeanLuminance));
            }
            catch (Exception ex)
            {
                snapshot.failed.Add(new AgentBatchFailure(item.Id, SafeReason(ex)));
            }
        }

        return new AgentImageStatsResult(stats, snapshot.failed);
    }

    public Task<AgentBatchResult> SetRatingAsync(IReadOnlyList<string> ids, int rating)
    {
        ValidateBatch(ids, BatchCap);
        if (rating is < 0 or > 5)
        {
            throw new AgentToolException("Rating must be between 0 and 5.");
        }

        return MutateAsync(ids, images => _vm.SetRatingForImagesAsync(images, rating));
    }

    public Task<AgentBatchResult> SetFlagAsync(IReadOnlyList<string> ids, string flag)
    {
        ValidateBatch(ids, BatchCap);
        var parsedFlag = AgentToolValidation.ParseFlag(flag);
        return MutateAsync(ids, images => _vm.SetFlagForImagesAsync(images, parsedFlag));
    }

    public Task<List<AgentPresetInfo>> ListPresetsAsync() => OnUiThreadAsync(() =>
        Task.FromResult(_vm.PresetService.AllPresets.Select(preset => new AgentPresetInfo(
            preset.Id,
            preset.Name,
            "User",
            true)).ToList()));

    public Task<AgentBatchResult> ApplyPresetAsync(
        IReadOnlyList<string> ids,
        string presetId)
    {
        ValidateBatch(ids, BatchCap);
        return OnUiThreadAsync(async () =>
        {
            var preset = _vm.PresetService.GetById(presetId) ??
                throw new AgentToolException("Unknown preset id; call list_presets.");
            var (images, failed) = ResolveImages(ids);
            failed.AddRange(await _vm.ApplyColorSettingsToImagesAsync(
                images, preset.Settings, preset.Id));
            return CreateBatchResult(images, failed);
        });
    }

    public Task<AgentBatchResult> ApplyEditSettingsAsync(
        IReadOnlyList<string> ids,
        AgentEditSettingsInput input)
    {
        ValidateBatch(ids, BatchCap);
        var patch = AgentEditSettingsMapper.CreatePatch(input);

        return MutateAsync(ids, images =>
            _vm.ApplyAgentEditSettingsToImagesAsync(images, patch));
    }

    public async Task<AgentExportResult> ExportImagesAsync(
        IReadOnlyList<string> ids,
        AgentExportOptions options)
    {
        ValidateBatch(ids, BatchCap);
        ValidateExportOptions(options);
        var format = AgentToolValidation.ParseExportFormat(options.Format);
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
                throw new AgentToolException("Output folder must be inside the currently open folder.");
            }

            var settings = AgentExportSettingsFactory.Create(
                output,
                options,
                format,
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
            throw new AgentToolException($"Could not create the output folder: {SafeReason(ex)}");
        }

        return await ExportResolvedImagesAsync(
            snapshot.images, snapshot.failed, snapshot.settings, variants, useSubfolders,
            snapshot.originalPaths);
    }

    private async Task<AgentExportResult> ExportResolvedImagesAsync(
        IReadOnlyList<ImageFile> images,
        List<AgentBatchFailure> failed,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        HashSet<string> originalPaths)
    {
        var exported = new List<string>();
        var skipped = new List<string>();
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var targets = new HashSet<string>(comparer);

        foreach (var image in images)
        {
            var pending = new List<(ExportVariant Variant, string RelativePath)>();
            foreach (var variant in variants)
            {
                var relativePath = useSubfolders
                    ? $"{variant.Name}/{settings.GetOutputFileName(image.FileName)}"
                    : settings.GetOutputFileName(image.FileName);
                try
                {
                    var target = Path.GetFullPath(
                        settings.GetOutputPath(image.FileName, variant, useSubfolders));
                    relativePath = ToOutputRelativePath(settings.OutputFolder, target);

                    if (!IsSameOrDescendant(settings.OutputFolder, target))
                    {
                        failed.Add(new AgentBatchFailure(
                            relativePath, "output path escapes export folder"));
                    }
                    else if (ExportSafety.IsOriginalPath(target, originalPaths))
                    {
                        failed.Add(new AgentBatchFailure(
                            relativePath, "would overwrite an original image"));
                    }
                    else if (File.Exists(target))
                    {
                        skipped.Add(relativePath);
                    }
                    else if (!targets.Add(target))
                    {
                        failed.Add(new AgentBatchFailure(
                            relativePath, "output name collision"));
                    }
                    else
                    {
                        pending.Add((variant, relativePath));
                    }
                }
                catch (Exception ex)
                {
                    failed.Add(new AgentBatchFailure(
                        relativePath.Replace('\\', '/'), SafeReason(ex)));
                }
            }

            if (pending.Count == 0) continue;
            try
            {
                await _imageService.ExportBatchAsync(
                    new[] { image }, settings, pending.Select(item => item.Variant).ToList(),
                    useSubfolders, null, CancellationToken.None);
                exported.AddRange(pending.Select(item => item.RelativePath));
            }
            catch (Exception ex)
            {
                failed.AddRange(pending.Select(item =>
                    new AgentBatchFailure(item.RelativePath, SafeReason(ex))));
            }
        }

        return new AgentExportResult(exported, skipped, failed);
    }

    private static IReadOnlyList<ExportVariant> CreateExportVariants(AgentExportOptions options)
    {
        if (options.Variants is not { Count: > 0 })
        {
            ValidateMaxDimension(options.MaxDimension);
            return new[] { new ExportVariant("export", options.MaxDimension) };
        }

        if (options.Variants.Count > 8)
            throw new AgentToolException("Variants must contain no more than 8 entries.");

        var names = new HashSet<string>(StringComparer.Ordinal);
        var variants = options.Variants.Select(item =>
        {
            var name = AgentToolValidation.SanitizeVariantName(item.Name);
            if (!names.Add(name))
                throw new AgentToolException("Variant names must be unique after sanitization.");
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
            throw new AgentToolException("Maximum dimension must be between 1 and 65536.");
    }

    private static string ToOutputRelativePath(string outputFolder, string fullPath) =>
        Path.GetRelativePath(outputFolder, fullPath).Replace('\\', '/');

    private Task<AgentBatchResult> MutateAsync(
        IReadOnlyList<string> ids,
        Func<IReadOnlyList<ImageFile>, Task<List<AgentBatchFailure>>> mutation) =>
        OnUiThreadAsync(async () =>
        {
            var (images, failed) = ResolveImages(ids);
            failed.AddRange(await mutation(images));
            return CreateBatchResult(images, failed);
        });

    private (List<ImageFile> Images, List<AgentBatchFailure> Failed) ResolveImages(
        IReadOnlyList<string> ids)
    {
        var byId = _vm.Library.AllImages.ToDictionary(
            image => image.FilePath, StringComparer.Ordinal);
        var images = new List<ImageFile>(ids.Count);
        var failed = new List<AgentBatchFailure>();
        foreach (var id in ids)
        {
            if (byId.TryGetValue(id, out var image))
            {
                images.Add(image);
            }
            else
            {
                failed.Add(new AgentBatchFailure(id, "unknown image id"));
            }
        }

        return (images, failed);
    }

    private static AgentBatchResult CreateBatchResult(
        IReadOnlyList<ImageFile> images,
        List<AgentBatchFailure> failed)
    {
        var failedIds = failed.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        return new AgentBatchResult(
            images.Where(image => !failedIds.Contains(image.FilePath))
                .Select(image => image.FilePath).ToList(),
            failed);
    }

    private AgentImageSummary MapImage(ImageFile image)
    {
        var membership = _vm.GetBurstMembership(image.FilePath);
        return new AgentImageSummary(
            image.FilePath, image.FileName, image.Rating,
            AgentToolValidation.FlagToString(image.Flag), image.HasEdits,
            image.MetadataLoaded, image.PixelWidth, image.PixelHeight, image.DateTaken,
            image.CameraDisplay, image.Iso, image.FNumber, image.ExposureTime,
            image.FocalLength, image.LensModel, membership?.BurstId,
            membership?.Index, membership?.Size);
    }

    private static bool MatchesFileType(ImageFile image, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || filter.Equals("all", StringComparison.OrdinalIgnoreCase) ||
        filter.Equals("raw", StringComparison.OrdinalIgnoreCase) && ImageFileTypeFilter.Raw.Matches(image) ||
        filter.Equals("jpeg", StringComparison.OrdinalIgnoreCase) && ImageFileTypeFilter.Jpeg.Matches(image);

    private static bool MatchesFlag(ImageFile image, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || filter.Equals("all", StringComparison.OrdinalIgnoreCase) ||
        AgentToolValidation.FlagToString(image.Flag).Equals(filter, StringComparison.OrdinalIgnoreCase);

    private static void ValidateListRequest(ListImagesRequest request)
    {
        if (request.Offset < 0) throw new AgentToolException("Offset cannot be negative.");
        if (request.Limit is < 1 or > 500) throw new AgentToolException("Limit must be between 1 and 500.");
        if (request.MinRating is < 0 or > 5) throw new AgentToolException("Minimum rating must be between 0 and 5.");
        if (!IsAllowed(request.FileType, "all", "raw", "jpeg"))
            throw new AgentToolException("File type must be all, raw, or jpeg.");
        if (!IsAllowed(request.Flag, "all", "picked", "rejected", "unflagged"))
            throw new AgentToolException("Flag must be all, picked, rejected, or unflagged.");
    }

    private static void ValidateExportOptions(AgentExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.NamingPattern))
            throw new AgentToolException("Naming pattern cannot be empty.");
        if (options.NamingPattern.IndexOfAny(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            throw new AgentToolException("Naming pattern must be a file name, not a path.");
    }

    private static void ValidateBatch(IReadOnlyList<string> ids, int cap)
    {
        if (ids == null) throw new AgentToolException("Image ids are required.");
        var error = AgentToolValidation.CheckBatchCap(ids, cap);
        if (error != null) throw new AgentToolException(error);
    }

    private static bool IsAllowed(string? value, params string[] allowed) =>
        string.IsNullOrWhiteSpace(value) ||
        allowed.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static bool IsSameOrDescendant(string root, string candidate)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
        return relative == "." ||
               (!Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string SafeReason(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;

    private static async Task<T> OnUiThreadAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) return await action();
        return await Dispatcher.UIThread.InvokeAsync(action);
    }
}

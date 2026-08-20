using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

public sealed record ExportWarning(
    ImageFile Image,
    string Code,
    string Message);

public sealed record ExportBatchResult
{
    public int ExportedCount { get; }
    public IReadOnlyList<ImageFile> FailedImages { get; }
    public IReadOnlyList<ExportWarning> Warnings { get; }

    public ExportBatchResult(
        int exportedCount,
        IReadOnlyList<ImageFile> failedImages,
        IReadOnlyList<ExportWarning>? warnings = null)
    {
        ExportedCount = exportedCount;
        FailedImages = failedImages;
        Warnings = warnings ?? Array.Empty<ExportWarning>();
    }
}

public sealed class ImageExportService
{
    private readonly RenderPipeline _renderPipeline;
    private readonly IBaseImageLoader _baseLoader;
    private readonly ExportMetadataService _metadataService;
    private readonly DcpProfileService _dcpProfiles;

    public ImageExportService(
        RenderPipeline renderPipeline,
        IBaseImageLoader baseLoader,
        ExportMetadataService metadataService) : this(
            renderPipeline,
            baseLoader,
            metadataService,
            new DcpProfileService(new SourceAvailabilityService()))
    {
    }

    internal ImageExportService(
        RenderPipeline renderPipeline,
        IBaseImageLoader baseLoader,
        ExportMetadataService metadataService,
        DcpProfileService dcpProfiles)
    {
        _renderPipeline = renderPipeline;
        _baseLoader = baseLoader;
        _metadataService = metadataService;
        _dcpProfiles = dcpProfiles ??
            throw new ArgumentNullException(nameof(dcpProfiles));
    }

    public Task<ExportBatchResult> ExportBatchAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var variants = settings.GetActiveVariants();
        return ExportBatchCoreAsync(
            images,
            settings,
            variants,
            useSubfolders: variants.Count > 1,
            progress,
            SourceReadIntent.Background,
            cancellationToken);
    }

    public async Task<int> ExportBatchAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default,
        IProgress<ExportWarning>? warningProgress = null)
    {
        var result = await ExportBatchCoreAsync(
            images,
            settings,
            variants,
            useSubfolders,
            progress,
            SourceReadIntent.Background,
            cancellationToken);
        foreach (var warning in result.Warnings)
        {
            warningProgress?.Report(warning);
        }
        return result.ExportedCount;
    }

    internal Task<ExportBatchResult> ExportBatchVariantsAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        CancellationToken cancellationToken) => ExportBatchCoreAsync(
            images,
            settings,
            variants,
            useSubfolders,
            progress: null,
            SourceReadIntent.Background,
            cancellationToken);

    internal async Task<int> ExportBatchApprovedAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        IProgress<(int current, int total, string fileName)>? progress,
        CancellationToken cancellationToken)
    {
        var result = await ExportBatchCoreAsync(
            images,
            settings,
            variants,
            useSubfolders,
            progress,
            SourceReadIntent.UserApprovedHydration,
            cancellationToken);
        return result.ExportedCount;
    }

    internal Task<ExportBatchResult> ExportBatchApprovedAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IProgress<(int current, int total, string fileName)>? progress,
        CancellationToken cancellationToken)
    {
        var variants = settings.GetActiveVariants();
        return ExportBatchCoreAsync(
            images,
            settings,
            variants,
            useSubfolders: variants.Count > 1,
            progress,
            SourceReadIntent.UserApprovedHydration,
            cancellationToken);
    }

    private async Task<ExportBatchResult> ExportBatchCoreAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        IProgress<(int current, int total, string fileName)>? progress,
        SourceReadIntent intent,
        CancellationToken cancellationToken)
    {
        var imageList = images.ToList();
        var outputColorSpace = settings.OutputColorSpace;
        var total = imageList.Count;
        var exported = 0;
        var failedImages = new List<ImageFile>();
        var warnings = new List<ExportWarning>();

        Directory.CreateDirectory(settings.OutputFolder);
        if (useSubfolders)
        {
            foreach (var variant in variants)
            {
                Directory.CreateDirectory(
                    Path.Combine(settings.OutputFolder, variant.Name));
            }
        }

        foreach (var imageFile in imageList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((exported, total, imageFile.FileName));

            var imageResult = await Task.Run(
                () => ExportImage(
                    imageFile,
                    settings,
                    variants,
                    useSubfolders,
                    outputColorSpace,
                    intent,
                    cancellationToken),
                cancellationToken);
            if (imageResult.WroteImage)
            {
                exported++;
                if (imageResult.Warning != null)
                {
                    warnings.Add(imageResult.Warning);
                }
            }
            else
            {
                failedImages.Add(imageFile);
            }
        }

        return new ExportBatchResult(exported, failedImages, warnings);
    }

    private ExportImageResult ExportImage(
        ImageFile imageFile,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        OutputColorSpace outputColorSpace,
        SourceReadIntent intent,
        CancellationToken cancellationToken)
    {
        if (variants.Count == 0)
        {
            return new ExportImageResult(false, null);
        }

        var stopwatch = Stopwatch.StartNew();
        var editSnapshot = imageFile.EditSettings.Clone();
        var decode = BaseDecodeSettings.From(editSnapshot);
        if (editSnapshot.RawProfile != null)
        {
            var resolution = _dcpProfiles.ResolveAsync(
                    imageFile,
                    editSnapshot.RawProfile,
                    forceRefresh: true,
                    cancellationToken)
                .GetAwaiter()
                .GetResult();
            decode = decode.WithProfileResolution(resolution);
        }
        using var baseImage = _baseLoader is GatedBaseImageLoader gatedLoader
            ? gatedLoader.LoadFullBase(
                imageFile,
                decode,
                intent,
                cancellationToken)
            : _baseLoader.LoadFullBase(
                imageFile,
                decode,
                cancellationToken);
        if (baseImage == null)
        {
            return new ExportImageResult(false, null);
        }

        var warning = CreateProfileWarning(imageFile, editSnapshot, baseImage.Info);

        cancellationToken.ThrowIfCancellationRequested();
        MagickImage? displayRec2020 = _renderPipeline.RenderDisplayRec2020(
            new RenderRequest(
            baseImage,
            editSnapshot,
            RenderIntent.Export,
            null,
            new RenderOptions(
                ComputeStats: false,
                ComputeOverlayMasks: false),
            outputColorSpace));
        baseImage.Dispose();
        try
        {
            var orderedVariants = variants
                .OrderBy(variant => variant.MaxDimension.HasValue ? 1 : 0)
                .ThenByDescending(variant => variant.MaxDimension ?? 0)
                .ToList();
            var fullLongEdge = Math.Max(
                displayRec2020.Width,
                displayRec2020.Height);
            var fullSize = $"{displayRec2020.Width}x{displayRec2020.Height}";

            for (var index = 0; index < orderedVariants.Count; index++)
            {
                var variant = orderedVariants[index];
                cancellationToken.ThrowIfCancellationRequested();
                var shared = displayRec2020 ??
                    throw new InvalidOperationException(
                        "The shared render was already consumed.");
                if (variant.MaxDimension is int maxDimension)
                {
                    RenderColorEncoding.ResizeInLinearLight(
                        shared,
                        maxDimension);
                }

                using var destination = index == orderedVariants.Count - 1
                    ? RenderFinalizer.FinalizeOwned(
                        Take(ref displayRec2020),
                        maxDimension: null,
                        outputColorSpace,
                        settings.OutputSharpening,
                        variant.MaxDimension is int ownedLongEdge &&
                        ownedLongEdge < fullLongEdge)
                    : RenderFinalizer.Finalize(
                        shared,
                        maxDimension: null,
                        outputColorSpace,
                        settings.OutputSharpening,
                        variant.MaxDimension is int sizedLongEdge &&
                        sizedLongEdge < fullLongEdge);
                _metadataService.Apply(
                    imageFile,
                    destination,
                    settings.StripLocationData,
                    intent);
                ExportEncoder.Write(
                    destination,
                    settings,
                    outputColorSpace,
                    settings.GetOutputPath(
                        imageFile.FileName,
                        variant,
                        useSubfolders));
            }

            LogPerformance(
                nameof(ImageExportService),
                nameof(ExportImage),
                stopwatch.ElapsedMilliseconds,
                imageFile.FilePath,
                $"variants={orderedVariants.Count};size={fullSize}");
            return new ExportImageResult(true, warning);
        }
        finally
        {
            displayRec2020?.Dispose();
        }
    }

    private static MagickImage Take(ref MagickImage? image)
    {
        var result = image ?? throw new InvalidOperationException(
            "The shared render was already consumed.");
        image = null;
        return result;
    }

    private static ExportWarning? CreateProfileWarning(
        ImageFile image,
        EditSettings settings,
        BaseImageInfo info)
    {
        if (settings.RawProfile == null ||
            info.ProfileStatus == DcpProfileErrorCode.None)
        {
            return null;
        }
        return new ExportWarning(
            image,
            $"profile_{info.ProfileStatus.ToString().ToLowerInvariant()}",
            info.ProfileMessage ??
                "The selected camera profile could not be applied; the built-in characterization was exported.");
    }

    private sealed record ExportImageResult(
        bool WroteImage,
        ExportWarning? Warning);
}

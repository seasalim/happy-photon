using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

public sealed record ExportBatchResult(
    int ExportedCount,
    IReadOnlyList<ImageFile> FailedImages);

public sealed class ImageExportService
{
    private readonly RenderPipeline _renderPipeline;
    private readonly IBaseImageLoader _baseLoader;
    private readonly ExportMetadataService _metadataService;

    public ImageExportService(
        RenderPipeline renderPipeline,
        IBaseImageLoader baseLoader,
        ExportMetadataService metadataService)
    {
        _renderPipeline = renderPipeline;
        _baseLoader = baseLoader;
        _metadataService = metadataService;
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
        CancellationToken cancellationToken = default)
    {
        var result = await ExportBatchCoreAsync(
            images,
            settings,
            variants,
            useSubfolders,
            progress,
            SourceReadIntent.Background,
            cancellationToken);
        return result.ExportedCount;
    }

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

            var wroteImage = await Task.Run(
                () => ExportImage(
                    imageFile,
                    settings,
                    variants,
                    useSubfolders,
                    outputColorSpace,
                    intent,
                    cancellationToken),
                cancellationToken);
            if (wroteImage)
            {
                exported++;
            }
            else
            {
                failedImages.Add(imageFile);
            }
        }

        return new ExportBatchResult(exported, failedImages);
    }

    private bool ExportImage(
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
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        var editSnapshot = imageFile.EditSettings.Clone();
        var decode = BaseDecodeSettings.From(editSnapshot);
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
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var rendered = _renderPipeline.Render(new RenderRequest(
            baseImage,
            editSnapshot,
            RenderIntent.Export,
            null,
            new RenderOptions(
                ComputeStats: false,
                ComputeOverlayMasks: false),
            outputColorSpace));
        baseImage.Dispose();
        var orderedVariants = variants
            .OrderBy(variant => variant.MaxDimension.HasValue ? 1 : 0)
            .ThenByDescending(variant => variant.MaxDimension ?? 0)
            .ToList();
        var fullLongEdge = Math.Max(
            rendered.Image.Width,
            rendered.Image.Height);

        foreach (var variant in orderedVariants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (variant.MaxDimension is int maxDimension)
            {
                RenderColorEncoding.ResizeInLinearLight(
                    rendered.Image,
                    maxDimension);
            }

            using var destination = new MagickImage(rendered.Image);
            RenderSharpening.ApplyOutput(
                destination,
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
            $"variants={orderedVariants.Count};" +
            $"size={rendered.Image.Width}x{rendered.Image.Height}");
        return true;
    }
}

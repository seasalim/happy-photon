using System.Diagnostics;
using HappyPhoton.Models;
using ImageMagick;
using static HappyPhoton.Services.ImageServiceHelpers;

namespace HappyPhoton.Services;

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

    public Task<int> ExportBatchAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var variants = settings.GetActiveVariants();
        return ExportBatchAsync(
            images,
            settings,
            variants,
            useSubfolders: variants.Count > 1,
            progress,
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
        var imageList = images.ToList();
        var total = imageList.Count;
        var exported = 0;

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
                    cancellationToken),
                cancellationToken);
            if (wroteImage)
            {
                exported++;
            }
        }

        progress?.Report((exported, total, "Complete"));
        return exported;
    }

    private bool ExportImage(
        ImageFile imageFile,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        CancellationToken cancellationToken)
    {
        if (variants.Count == 0)
        {
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        var editSnapshot = imageFile.EditSettings.Clone();
        var decode = BaseDecodeSettings.From(editSnapshot);
        using var baseImage = _baseLoader.LoadFullBase(
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
                ComputeOverlayMasks: false)));
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
                settings.StripLocationData);
            ExportEncoder.Write(
                destination,
                settings,
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

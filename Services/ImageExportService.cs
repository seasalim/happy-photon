using ImageMagick;
using HappyPhoton.Models;
using static HappyPhoton.Services.ImageServiceHelpers;
using static HappyPhoton.Services.BitmapConversionService;

namespace HappyPhoton.Services;

/// <summary>
/// Service for exporting images with applied edits.
/// </summary>
public class ImageExportService
{
    private readonly EditApplicationService _editService;
    private readonly IRawProcessingService _rawService;

    public ImageExportService(EditApplicationService editService, IRawProcessingService rawService)
    {
        _editService = editService;
        _rawService = rawService;
    }

    public async Task<int> ExportBatchAsync(
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var variants = settings.GetActiveVariants();
        return await ExportBatchAsync(images, settings, variants,
            useSubfolders: variants.Count > 1, progress, cancellationToken);
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
                Directory.CreateDirectory(Path.Combine(settings.OutputFolder, variant.Name));
            }
        }

        var jpegSizeHint = variants.Count > 0 && variants.All(v => v.MaxDimension.HasValue)
            ? variants.Max(v => v.MaxDimension!.Value) * 2
            : (int?)null;

        foreach (var imageFile in imageList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report((exported, total, imageFile.FileName));

            await Task.Run(() =>
            {
                MagickImage? image = null;

                // For RAW files, use LibRaw to match preview processing pipeline
                if (imageFile.IsRaw && _rawService.IsAvailable)
                {
                    image = _rawService.DecodeFull(imageFile.FilePath);
                    if (image != null)
                    {
                        LogDebug(nameof(ExportBatchAsync), $"LibRaw decoded: {image.Width}x{image.Height}", imageFile.FilePath);
                    }
                }

                // Fallback to MagickImage for non-RAW files or if LibRaw fails
                if (image == null)
                {
                    var readSettings = new MagickReadSettings();

                    if (jpegSizeHint.HasValue)
                    {
                        var ext = Path.GetExtension(imageFile.FilePath).ToUpperInvariant();
                        if (ext == ".JPG" || ext == ".JPEG")
                        {
                            ApplyJpegSizeHint(readSettings, jpegSizeHint.Value);
                        }
                    }

                    image = new MagickImage(imageFile.FilePath, readSettings);
                    image.AutoOrient();
                    LogDebug(nameof(ExportBatchAsync), $"MagickImage loaded: {image.Width}x{image.Height}", imageFile.FilePath);
                }

                using (image)
                {
                    var editSettings = imageFile.EditSettings;
                    LogDebug(nameof(ExportBatchAsync),
                        $"EditSettings: Rotation={editSettings.Rotation}, Horizon={editSettings.HorizonRotation}, Crop={editSettings.Crop != null}, CropIsFullImage={editSettings.Crop?.IsFullImage ?? true}",
                        imageFile.FilePath);

                    _editService.ApplyEdits(image, imageFile.EditSettings);

                    image.Format = settings.Format switch
                    {
                        ExportFormat.Png => MagickFormat.Png,
                        ExportFormat.Webp => MagickFormat.WebP,
                        _ => MagickFormat.Jpeg
                    };
                    image.Quality = (uint)settings.Quality;

                    foreach (var variant in variants)
                    {
                        if (variant.MaxDimension is int maxDimension)
                        {
                            _editService.ApplyResize(image, maxDimension);
                        }

                        image.Write(settings.GetOutputPath(imageFile.FileName, variant, useSubfolders));
                    }
                }
            }, cancellationToken);

            exported++;
        }

        progress?.Report((exported, total, "Complete"));
        return exported;
    }
}

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using ImageMagick;

namespace HappyPhoton.Tests;

/// <summary>
/// Short forms of service calls that only tests use. Each one forwards to the
/// full-signature production call with the defaults production code passes.
/// </summary>
internal static class ServiceTestConveniences
{
    private static ThumbnailSizeRequest Medium =>
        ThumbnailSizeRequest.For(BrowseThumbnailSize.Medium);

    public static ImageService CreateImageService(CatalogService catalog)
    {
        var health = LibRawNativeSupport.Health;

        return new ImageService(
            catalog,
            new BaseLoaderRouter(new RawBaseLoader(health), new StandardBaseLoader()),
            new SourceAvailabilityService(),
            health);
    }

    public static Task<(Bitmap? preview, HistogramData histogram)> LoadPreviewWithHistogramAsync(
        this PreviewService service,
        ImageFile imageFile,
        EditSettings settings,
        bool skipHistogram = false,
        CancellationToken cancellationToken = default) =>
        service.LoadPreviewWithHistogramAsync(
            imageFile,
            settings,
            Medium,
            skipHistogram,
            cancellationToken);

    public static async Task<(Bitmap? preview, HistogramData histogram)> LoadPreviewWithHistogramAsync(
        this PreviewService service,
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram = false,
        CancellationToken cancellationToken = default)
    {
        using var artifacts = await service.LoadPreviewArtifactsAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            ClippingOverlaySide.None,
            cancellationToken).ConfigureAwait(false);

        return DetachPreview(artifacts);
    }

    public static Task<(Bitmap? preview, HistogramData histogram)> ApplyEditsToPreviewAsync(
        this PreviewService service,
        ImageFile imageFile,
        EditSettings settings,
        bool skipHistogram = false,
        CancellationToken cancellationToken = default) =>
        service.ApplyEditsToPreviewAsync(
            imageFile,
            settings,
            Medium,
            skipHistogram,
            cancellationToken);

    public static async Task<(Bitmap? preview, HistogramData histogram)> ApplyEditsToPreviewAsync(
        this PreviewService service,
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram = false,
        CancellationToken cancellationToken = default)
    {
        using var artifacts = await service.ApplyEditsToPreviewArtifactsAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            ClippingOverlaySide.None,
            cancellationToken).ConfigureAwait(false);

        return DetachPreview(artifacts);
    }

    public static Task<Bitmap?> TryPromoteRenderedThumbnailAsync(
        this PreviewService service,
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken = default) =>
        service.TryPromoteRenderedThumbnailAsync(
            imageFile,
            settings,
            Medium,
            cancellationToken);

    public static Task<ThumbnailLoadResult> LoadThumbnailAsync(
        this ThumbnailService service,
        ImageFile imageFile,
        CancellationToken cancellationToken = default) =>
        service.LoadThumbnailAsync(imageFile, Medium, cancellationToken);

    public static Task<ThumbnailLoadResult> LoadThumbnailAsync(
        this ImageService service,
        ImageFile imageFile,
        CancellationToken cancellationToken) =>
        service.LoadThumbnailAsync(imageFile, Medium, cancellationToken);

    public static Task<ThumbnailLoadResult> LoadUneditedThumbnailAsync(
        this ThumbnailService service,
        ImageFile imageFile,
        CancellationToken cancellationToken = default) =>
        service.LoadUneditedThumbnailAsync(imageFile, Medium, cancellationToken);

    public static Task<ThumbnailLoadResult> LoadUneditedThumbnailAsync(
        this ThumbnailService service,
        ImageFile imageFile,
        ThumbnailSizeRequest request,
        CancellationToken cancellationToken = default) =>
        service.LoadUneditedThumbnailAsync(
            imageFile,
            request,
            allowUndersizedCachePlaceholder: true,
            SourceReadIntent.Background,
            cancellationToken);

    public static Bitmap? LoadFromCache(
        this ThumbnailCacheService cache,
        ImageFile imageFile) =>
        cache.LoadFromCache(
            imageFile,
            new ThumbnailSizeRequest(
                ThumbnailService.ThumbnailSize,
                ThumbnailService.ThumbnailSize),
            out _,
            out _);

    public static Bitmap? LoadMatching(
        this RenderedThumbnailCacheService cache,
        ImageFile imageFile,
        string settingsHash) =>
        cache.LoadMatching(imageFile, settingsHash, Medium, out _);

    public static void QueueSaveToCache(
        this PreviewCacheService cache,
        ImageFile imageFile,
        MagickImage image,
        string settingsHash) =>
        _ = cache.QueueSaveToCache(imageFile, image, settingsHash, identity: null);

    public static void QueueSaveToCache(
        this PreviewCacheService cache,
        ImageFile imageFile,
        Bitmap bitmap,
        string settingsHash,
        DateTime? sourceWriteTime = null) =>
        cache.QueueSaveToCache(
            imageFile,
            bitmap,
            settingsHash,
            identity: null,
            sourceWriteTime,
            ownsBitmap: false);

    public static Task SaveEditSettingsBatchAsync(
        this CatalogService catalog,
        IReadOnlyList<CatalogEditSettingsUpdate> updates) =>
        catalog.SaveEditSettingsBatchWithHistoryAsync(updates, operation: string.Empty);

    public static BaseImage? LoadPreviewBase(
        this IBaseImageLoader loader,
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        loader.LoadPreviewBaseWithOutcome(file, decode, cancellationToken)
            .DetachInteractiveImage();

    public static BaseImage? DetachInteractiveImage(this BaseImageLoadOutcome outcome)
    {
        if (outcome.Pair is not { } pair)
        {
            return null;
        }

        using (pair)
        {
            return pair.DetachInteractive();
        }
    }

    extension(BaseImageLoadOutcome)
    {
        public static BaseImageLoadOutcome FromImage(
            BaseImage? image,
            BaseImageLoadFailure failure) =>
            image != null
                ? BaseImageLoadOutcome.Loaded(image)
                : BaseImageLoadOutcome.Failed(failure);

        public static BaseImageLoadOutcome FromImage(
            BaseImage? image,
            BaseImageLoadFailure failure,
            PreviewSourceAnalysis analysis) =>
            image != null
                ? BaseImageLoadOutcome.Loaded(new PreviewBasePair(image, large: null), analysis)
                : BaseImageLoadOutcome.Failed(failure);
    }

    public static async Task<int> ExportBatchAsync(
        this ImageExportService service,
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IReadOnlyList<ExportVariant> variants,
        bool useSubfolders,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default,
        IProgress<ExportWarning>? warningProgress = null)
    {
        var job = settings.CreateJob(images, variants, useSubfolders);
        var result = await service.ExportBatchAsync(job, progress, cancellationToken);

        foreach (var warning in result.Warnings)
        {
            warningProgress?.Report(warning);
        }

        return result.ExportedCount;
    }

    public static Task<ExportBatchResult> ExportBatchApprovedAsync(
        this ImageExportService service,
        IEnumerable<ImageFile> images,
        ExportSettings settings,
        IProgress<(int current, int total, string fileName)>? progress,
        CancellationToken cancellationToken) =>
        service.ExportBatchApprovedAsync(
            settings.CreateJob(images),
            progress,
            cancellationToken);

    public static Task<ExportBatchResult> ExportBatchAsync(
        this MainWindowViewModel viewModel,
        IReadOnlyList<ImageFile> imagesToExport,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default) =>
        viewModel.ExportJobAsync(
            viewModel.ExportSettings.CreateJob(imagesToExport),
            hydrationApproved: false,
            progress,
            cancellationToken);

    public static Task<ExportBatchResult> ExportBatchApprovedAsync(
        this MainWindowViewModel viewModel,
        IReadOnlyList<ImageFile> imagesToExport,
        IProgress<(int current, int total, string fileName)>? progress = null,
        CancellationToken cancellationToken = default) =>
        viewModel.ExportJobAsync(
            viewModel.ExportSettings.CreateJob(imagesToExport),
            hydrationApproved: true,
            progress,
            cancellationToken);

    extension(ExportBatchResult result)
    {
        // Export runs captures in job order, so failed outcomes list captures in that order.
        public IReadOnlyList<ImageFile> FailedImages =>
            result.FailedTargets
                .Select(outcome => outcome.Capture)
                .Distinct(ReferenceEqualityComparer.Instance)
                .Cast<ImageFile>()
                .ToArray();
    }

    // The browse footer keeps its own name scope; find its controls through it.
    public static T? FindFooterControl<T>(this BrowseGridView grid, string name)
        where T : Control =>
        grid.FindControl<BrowseGridFooter>("BrowseGridFooter")!.FindControl<T>(name);

    private static (Bitmap? preview, HistogramData histogram) DetachPreview(
        PreviewArtifacts artifacts)
    {
        artifacts.CommitPromotion();

        return (artifacts.DetachBitmap(), artifacts.Histogram);
    }
}

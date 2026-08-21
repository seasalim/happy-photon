using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    public Task<PreviewArtifacts> LoadPreviewArtifactsAsync(
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram,
        ClippingOverlaySide overlaySides,
        CancellationToken cancellationToken = default)
    {
        QueueRenderedPreviewIfLeaving(imageFile);
        return RenderAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            overlaySides,
            forceProfileRefresh: true,
            cancellationToken);
    }

    public Task<PreviewArtifacts> ApplyEditsToPreviewArtifactsAsync(
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram,
        ClippingOverlaySide overlaySides,
        CancellationToken cancellationToken = default) =>
        RenderAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            overlaySides,
            forceProfileRefresh: false,
            cancellationToken);

    public Task<(Avalonia.Media.Imaging.Bitmap? preview,
        HistogramData histogram)> LoadPreviewWithHistogramAsync(
        ImageFile imageFile,
        EditSettings settings,
        bool skipHistogram = false,
        CancellationToken cancellationToken = default) =>
        LoadPreviewWithHistogramAsync(
            imageFile,
            settings,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            skipHistogram,
            cancellationToken);

    public async Task<(Avalonia.Media.Imaging.Bitmap? preview,
        HistogramData histogram)> LoadPreviewWithHistogramAsync(
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram = false,
        CancellationToken cancellationToken = default)
    {
        using var artifacts = await LoadPreviewArtifactsAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            ClippingOverlaySide.None,
            cancellationToken).ConfigureAwait(false);
        return artifacts.DetachLegacyResult();
    }

    public Task<(Avalonia.Media.Imaging.Bitmap? preview,
        HistogramData histogram)> ApplyEditsToPreviewAsync(
        ImageFile imageFile,
        EditSettings settings,
        bool skipHistogram = false,
        CancellationToken cancellationToken = default) =>
        ApplyEditsToPreviewAsync(
            imageFile,
            settings,
            ThumbnailSizeRequest.For(LibraryThumbnailSize.Medium),
            skipHistogram,
            cancellationToken);

    public async Task<(Avalonia.Media.Imaging.Bitmap? preview,
        HistogramData histogram)> ApplyEditsToPreviewAsync(
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram = false,
        CancellationToken cancellationToken = default)
    {
        using var artifacts = await ApplyEditsToPreviewArtifactsAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            ClippingOverlaySide.None,
            cancellationToken).ConfigureAwait(false);
        return artifacts.DetachLegacyResult();
    }
}

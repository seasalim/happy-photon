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
        CancellationToken cancellationToken = default,
        long? surfaceGeneration = null,
        bool computeWaveform = false)
    {
        QueueRenderedPreviewIfLeaving(imageFile);
        return RenderAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            computeWaveform,
            overlaySides,
            forceProfileRefresh: true,
            cancellationToken,
            surfaceGeneration);
    }

    public Task<PreviewArtifacts> ApplyEditsToPreviewArtifactsAsync(
        ImageFile imageFile,
        EditSettings settings,
        ThumbnailSizeRequest thumbnailRequest,
        bool skipHistogram,
        ClippingOverlaySide overlaySides,
        CancellationToken cancellationToken = default,
        long? surfaceGeneration = null,
        bool computeWaveform = false) =>
        RenderAsync(
            imageFile,
            settings,
            thumbnailRequest,
            skipHistogram,
            computeWaveform,
            overlaySides,
            forceProfileRefresh: false,
            cancellationToken,
            surfaceGeneration);
}

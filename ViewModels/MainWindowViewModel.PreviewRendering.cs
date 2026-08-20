using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private async Task UpdatePreviewWithCurrentSliders(
        bool skipHistogram = false,
        CancellationToken cancellationToken = default)
    {
        var selectedImage = SelectedImage;
        if (selectedImage == null || !CanEditSelectedImage) return;
        SignalBackgroundActivityStarted();

        var tempSettings = selectedImage.EditSettings.Clone();
        SaveSlidersTo(tempSettings);
        tempSettings.Rotation = Rotation;
        tempSettings.HorizonRotation = HorizonRotation;
        tempSettings.Crop = PreviewCrop();
        tempSettings.Curve = CurrentCurve ?? new CurveData();

        using var artifacts = await ImageService.ApplyEditsToPreviewArtifactsAsync(
            selectedImage,
            tempSettings,
            LibraryThumbnailRequest,
            skipHistogram,
            RequestedClippingOverlaySides,
            cancellationToken);
        var preview = artifacts.DetachBitmap();

        if (preview == null || cancellationToken.IsCancellationRequested ||
            SelectedImage != selectedImage ||
            (!IsDevelopMode && !IsFullScreenMode))
        {
            preview?.Dispose();
            if (preview == null &&
                !cancellationToken.IsCancellationRequested &&
                ReferenceEquals(SelectedImage, selectedImage))
            {
                ClearPreviewClippingArtifacts();
            }
            return;
        }

        IsShowingOriginal = false;
        ReconcileHighlightReconstructionCapability(
            selectedImage,
            artifacts.IsRawSource);
        InstallPreviewClipping(artifacts);
        ReplacePreviewImage(preview, PreviewPaintSource.FreshRender);

        if (!skipHistogram)
        {
            Histogram = artifacts.Histogram;
        }
    }
}

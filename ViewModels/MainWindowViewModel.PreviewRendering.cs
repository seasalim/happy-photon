using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private async Task<bool> UpdatePreviewWithCurrentSliders(
        CancellationToken cancellationToken = default,
        long? generation = null,
        PreviewSurfaceIntent intent = PreviewSurfaceIntent.Edited,
        bool promotable = true,
        PreviewSurfaceIntent? rollbackRequestedIntent = null)
    {
        var selectedImage = SelectedImage;
        if (selectedImage == null || !CanEditSelectedImage) return false;
        var previousIntent = _requestedPreviewIntent;
        var outcomeGeneration = generation ??
            ReserveRenderOutcome(intent);
        SignalBackgroundActivityStarted();

        var tempSettings = selectedImage.EditSettings.Clone();
        SaveSlidersTo(tempSettings);
        tempSettings.Rotation = Rotation;
        tempSettings.HorizonRotation = HorizonRotation;
        tempSettings.Crop = PreviewCrop();

        try
        {
            if (IsExportMode && ExportSettings.ShowProof)
            {
                return await LoadExportProofAsync(
                    selectedImage,
                    tempSettings,
                    outcomeGeneration,
                    cancellationToken);
            }

            using var artifacts = await ImageService.Previews
                .ApplyEditsToPreviewArtifactsAsync(
                    selectedImage,
                    tempSettings,
                    BrowseThumbnailRequest,
                    skipHistogram: false,
                    RequestedClippingOverlaySides,
                    cancellationToken,
                    outcomeGeneration,
                    computeWaveform: IsWaveformScopeEffective);
            cancellationToken.ThrowIfCancellationRequested();
            var succeeded = artifacts.Bitmap != null;
            var painted = ApplyRenderOutcome(RenderOutcome.FromArtifacts(
                selectedImage,
                outcomeGeneration,
                intent,
                RenderOutcomeClass.StateDefining,
                PreviewPaintSource.FreshRender,
                artifacts,
                promotable,
                rollbackRequestedIntent ?? previousIntent));
            if (!succeeded)
            {
                var rolledBack = await RollbackFailedRenderAsync(
                    selectedImage,
                    outcomeGeneration,
                    previousIntent);
                return !rolledBack;
            }
            if (painted && promotable &&
                intent == PreviewSurfaceIntent.Edited)
            {
                _lastAppliedEditSettings = tempSettings.Clone();
            }
            return painted;
        }
        catch (OperationCanceledException)
        {
            if (outcomeGeneration == Volatile.Read(
                    ref _latestPreviewOutcomeGeneration))
            {
                await RollbackFailedRenderAsync(
                    selectedImage,
                    outcomeGeneration,
                    previousIntent);
            }
            return false;
        }
    }

    private async Task<bool> RollbackFailedRenderAsync(
        ImageFile image,
        long generation,
        PreviewSurfaceIntent previousIntent)
    {
        if (_lastAppliedEditSettings == null)
        {
            return false;
        }
        var previousSettings = _lastAppliedEditSettings.Clone();
        if (!RollbackEditReservation(
                image,
                previousSettings,
                generation,
                previousIntent))
        {
            return false;
        }

        await SaveEditSettingsAsync(image, previousSettings);
        return true;
    }
}

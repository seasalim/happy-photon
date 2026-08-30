using HappyPhoton.Models;

namespace HappyPhoton.ViewModels;

public partial class MainWindowViewModel
{
    private async Task<bool> UpdatePreviewWithCurrentSliders(
        CancellationToken cancellationToken = default,
        long? generation = null,
        PreviewSurfaceIntent intent = PreviewSurfaceIntent.Edited,
        bool promotable = true,
        PreviewSurfaceIntent? rollbackRequestedIntent = null,
        Action<bool>? observeRenderSucceeded = null)
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
                var proofSucceeded = await LoadExportProofAsync(
                    selectedImage,
                    tempSettings,
                    outcomeGeneration,
                    cancellationToken);
                observeRenderSucceeded?.Invoke(proofSucceeded);
                return proofSucceeded;
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
            observeRenderSucceeded?.Invoke(succeeded);
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
            _hasPromotableEditedRender |= painted && promotable &&
                intent == PreviewSurfaceIntent.Edited;
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
        if (!_hasPromotableEditedRender || _lastSavedState == null)
        {
            return false;
        }
        var previousSettings = _lastSavedState.Clone();
        var needsSave = !image.EditSettings.HasSameEdits(previousSettings);
        if (!RollbackEditReservation(
                image,
                previousSettings,
                generation,
                previousIntent))
        {
            return false;
        }

        if (needsSave)
            await SaveEditSettingsAsync(
                image, previousSettings, recordHistory: false);
        return true;
    }
}

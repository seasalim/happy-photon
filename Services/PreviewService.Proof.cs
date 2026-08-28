using Avalonia.Media.Imaging;
using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    internal Func<RenderRequest, MagickImage>? ProofRenderDisplayRec2020
    {
        get;
        set;
    }

    internal async Task<Bitmap?> RenderProofAsync(
        ImageFile imageFile,
        EditSettings settings,
        int? maxDimension,
        OutputColorSpace outputColorSpace,
        OutputSharpeningMode outputSharpening,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageFile);
        ArgumentNullException.ThrowIfNull(settings);
        if (maxDimension is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDimension));
        }
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var settingsSnapshot = settings.Clone();
        await imageFile.EnsureCatalogIdAsync(_catalogService);
        var decode = await ResolveDecodeAsync(
            imageFile,
            settingsSnapshot,
            cancellationToken,
            forceProfileRefresh: true).ConfigureAwait(false);

        return await Task.Run(() =>
        {
            using var baseImage = _baseLoader.LoadFullBase(
                imageFile, decode, cancellationToken);
            if (baseImage == null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var request = new RenderRequest(
                baseImage,
                settingsSnapshot,
                RenderIntent.Export,
                MaxDimension: null,
                new RenderOptions(false, false));
            using var upstream = (ProofRenderDisplayRec2020 ??
                _renderPipeline.RenderDisplayRec2020)(request);
            using var finalized = RenderFinalizer.FinalizeProof(
                upstream,
                maxDimension,
                outputColorSpace,
                outputSharpening,
                settingsSnapshot.Effects);
            cancellationToken.ThrowIfCancellationRequested();
            return BitmapConversionService.ConvertToBitmap(finalized);
        }, cancellationToken).ConfigureAwait(false);
    }
}

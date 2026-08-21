using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class PreviewLoadOutcome : EventArgs
{
    public ImageFile ImageFile { get; }
    public long Generation { get; }
    public BaseImageLoadFailure Failure { get; }
    public bool Succeeded => Failure == BaseImageLoadFailure.None;

    internal PreviewLoadOutcome(
        ImageFile imageFile,
        long generation,
        BaseImageLoadFailure failure)
    {
        ImageFile = imageFile;
        Generation = generation;
        Failure = failure;
    }
}

public sealed partial class PreviewService
{
    public event EventHandler<PreviewLoadOutcome>? PreviewLoadCompleted;

    private async Task<PreviewBaseAcquisition?> AcquirePreviewBaseAsync(
        ImageFile imageFile,
        BaseDecodeSettings decode,
        long renderGeneration,
        long outcomeGeneration,
        long? surfaceGeneration,
        CancellationToken cancellationToken)
    {
        var result = await _baseCoordinator.GetPreviewResultAsync(
            imageFile,
            decode,
            cancellationToken,
            surfaceGeneration).ConfigureAwait(false);
        if (result.Acquisition == null &&
            !result.Superseded &&
            renderGeneration == Volatile.Read(ref _renderGeneration))
        {
            ReportPreviewOutcome(imageFile, outcomeGeneration, result.Failure);
        }
        return result.Acquisition;
    }

    private void ReportPreviewSuccess(ImageFile imageFile, long generation) =>
        ReportPreviewOutcome(
            imageFile,
            generation,
            BaseImageLoadFailure.None);

    private void ReportPreviewFailure(ImageFile imageFile, long generation)
    {
        ReportPreviewOutcome(
            imageFile,
            generation,
            BaseImageLoadFailure.DecodeFailed);
    }

    private void ReportPreviewOutcome(
        ImageFile imageFile,
        long generation,
        BaseImageLoadFailure failure)
    {
        PreviewLoadCompleted?.Invoke(
            this,
            new PreviewLoadOutcome(imageFile, generation, failure));
    }
}

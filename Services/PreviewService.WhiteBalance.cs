using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

public sealed partial class PreviewService
{
    public async Task<WhiteBalanceBaseContext?> GetWhiteBalanceContextAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var snapshot = await _baseCoordinator.GetPreviewAsync(
            imageFile,
            BaseDecodeSettings.From(settings),
            cancellationToken);
        return snapshot == null
            ? null
            : new WhiteBalanceBaseContext(
                snapshot.Base.Info.AsShotKelvin,
                snapshot.Base.Info.AsShotTint,
                snapshot.Base.Info.IsRawSource);
    }

    public Task<double[]?> GetAutoWhiteBalanceAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken = default) =>
        SampleWhiteBalanceAsync(
            imageFile,
            settings,
            WhiteBalanceSampling.AutoGains,
            cancellationToken);

    public Task<double[]?> PickWhiteBalanceAsync(
        ImageFile imageFile,
        EditSettings settings,
        double normalizedX,
        double normalizedY,
        CancellationToken cancellationToken = default) =>
        SampleWhiteBalanceAsync(
            imageFile,
            settings,
            image => WhiteBalanceSampling.PickGains(
                image,
                settings,
                normalizedX,
                normalizedY),
            cancellationToken);

    private async Task<double[]?> SampleWhiteBalanceAsync(
        ImageFile imageFile,
        EditSettings settings,
        Func<MagickImage, double[]?> sample,
        CancellationToken cancellationToken)
    {
        using var snapshot = await _baseCoordinator.GetPreviewAsync(
            imageFile,
            BaseDecodeSettings.From(settings),
            cancellationToken);
        if (snapshot == null)
        {
            return null;
        }

        return await Task.Run(
            () => sample(snapshot.Base.Pixels),
            cancellationToken);
    }
}

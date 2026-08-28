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
            await ResolveDecodeAsync(imageFile, settings, cancellationToken),
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
        GetSampleGainsAsync(GetAutoWhiteBalanceSampleAsync(
            imageFile,
            settings,
            cancellationToken));

    internal Task<WhiteBalanceSample?> GetAutoWhiteBalanceSampleAsync(
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
        GetSampleGainsAsync(PickWhiteBalanceSampleAsync(
            imageFile,
            settings,
            normalizedX,
            normalizedY,
            cancellationToken));

    internal Task<WhiteBalanceSample?> PickWhiteBalanceSampleAsync(
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

    private async Task<WhiteBalanceSample?> SampleWhiteBalanceAsync(
        ImageFile imageFile,
        EditSettings settings,
        Func<MagickImage, double[]?> sample,
        CancellationToken cancellationToken)
    {
        using var snapshot = await _baseCoordinator.GetPreviewAsync(
            imageFile,
            await ResolveDecodeAsync(imageFile, settings, cancellationToken),
            cancellationToken);
        if (snapshot == null)
        {
            return null;
        }
        if (WhiteBalanceSampleGateAsync is { } gate)
        {
            await gate().ConfigureAwait(false);
        }

        var gains = await Task.Run(
            () => sample(snapshot.Base.Pixels),
            cancellationToken);
        return gains == null
            ? null
            : new WhiteBalanceSample(gains, snapshot.Base);
    }

    internal async Task<bool> IsWhiteBalanceBaseCurrentAsync(
        ImageFile imageFile,
        EditSettings settings,
        object baseToken,
        CancellationToken cancellationToken = default)
    {
        var decode = await ResolveDecodeAsync(
            imageFile,
            settings,
            cancellationToken).ConfigureAwait(false);
        using var snapshot = _baseCoordinator.TryAcquireCurrent(
            imageFile,
            decode);
        return snapshot != null && ReferenceEquals(snapshot.Base, baseToken);
    }

    private static async Task<double[]?> GetSampleGainsAsync(
        Task<WhiteBalanceSample?> sample) =>
        (await sample.ConfigureAwait(false))?.Gains;

    // Bases are keyed by the profile selection token, so any decode this
    // service initiates must carry the resolved profile — a selection-only
    // decode would install a profile-less base under the resolved key.
    private async Task<BaseDecodeSettings> ResolveDecodeAsync(
        ImageFile imageFile,
        EditSettings settings,
        CancellationToken cancellationToken,
        bool forceProfileRefresh = false)
    {
        var decode = BaseDecodeSettings.From(settings);
        if (settings.RawProfile == null)
        {
            return decode;
        }

        var resolution = await _dcpProfiles.ResolveAsync(
            imageFile,
            settings.RawProfile,
            forceRefresh: forceProfileRefresh,
            cancellationToken).ConfigureAwait(false);
        return decode.WithProfileResolution(resolution);
    }
}

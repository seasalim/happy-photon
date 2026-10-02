using HappyPhoton.Models;
using ImageMagick;

namespace HappyPhoton.Services;

internal sealed record PreviewBaseSample<T>(T Value, object BaseToken);

public sealed partial class PreviewService
{
    internal async Task<PreviewBaseSample<T>?> SamplePreviewBaseAsync<T>(
        ImageFile imageFile,
        EditSettings settings,
        Func<MagickImage, T> sample,
        CancellationToken cancellationToken = default,
        Func<Task>? gate = null)
    {
        using var snapshot = await _baseCoordinator.GetPreviewAsync(
            imageFile,
            await ResolveDecodeAsync(imageFile, settings, cancellationToken),
            cancellationToken);
        if (snapshot == null) return null;

        if (gate != null)
        {
            await gate().WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var value = await Task.Run(() => sample(snapshot.Base.Pixels), cancellationToken);

        return new PreviewBaseSample<T>(value, snapshot.Base);
    }

    internal async Task<bool> IsPreviewBaseCurrentAsync(
        ImageFile imageFile,
        EditSettings settings,
        object baseToken,
        CancellationToken cancellationToken = default)
    {
        var decode = await ResolveDecodeAsync(
            imageFile, settings, cancellationToken).ConfigureAwait(false);
        using var snapshot = _baseCoordinator.TryAcquireCurrent(imageFile, decode);

        return snapshot != null && ReferenceEquals(snapshot.Base, baseToken);
    }
}

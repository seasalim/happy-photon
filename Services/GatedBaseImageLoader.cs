using HappyPhoton.Models;

namespace HappyPhoton.Services;

internal sealed class GatedBaseImageLoader : IBaseImageLoader
{
    private readonly IBaseImageLoader _inner;
    private readonly ISourceAvailabilityService _availabilityService;

    internal GatedBaseImageLoader(
        IBaseImageLoader inner,
        ISourceAvailabilityService availabilityService)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _availabilityService = availabilityService ??
            throw new ArgumentNullException(nameof(availabilityService));
    }

    public bool CanLoad(ImageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return _inner.CanLoad(file);
    }

    public BaseImage? LoadPreviewBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        LoadPreviewBaseWithOutcome(
            file,
            decode,
            cancellationToken).Image;

    public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SourceAccessPolicy.CanRead(
            _availabilityService.GetAvailability(file.FilePath),
            SourceReadIntent.Background))
        {
            return BaseImageLoadOutcome.Failed(
                BaseImageLoadFailure.SourceUnavailable);
        }

        return _inner.LoadPreviewBaseWithOutcome(
            file,
            decode,
            cancellationToken);
    }

    public BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        LoadFullBase(
            file,
            decode,
            SourceReadIntent.Background,
            cancellationToken);

    internal BaseImage? LoadPreviewBase(
        ImageFile file,
        BaseDecodeSettings decode,
        SourceReadIntent intent,
        CancellationToken cancellationToken) =>
        Load(file, intent, cancellationToken, () => _inner.LoadPreviewBase(
            file,
            decode,
            cancellationToken));

    internal BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        SourceReadIntent intent,
        CancellationToken cancellationToken) =>
        Load(file, intent, cancellationToken, () => _inner.LoadFullBase(
            file,
            decode,
            cancellationToken));

    private BaseImage? Load(
        ImageFile file,
        SourceReadIntent intent,
        CancellationToken cancellationToken,
        Func<BaseImage?> load)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();
        if (!SourceAccessPolicy.CanRead(
            _availabilityService.GetAvailability(file.FilePath),
            intent))
        {
            return null;
        }

        return load();
    }
}

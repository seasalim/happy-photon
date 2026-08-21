using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class BaseLoaderRouter : IBaseImageLoader
{
    private readonly IBaseImageLoader _rawLoader;
    private readonly IBaseImageLoader _standardLoader;

    public BaseLoaderRouter(
        IBaseImageLoader rawLoader,
        IBaseImageLoader standardLoader)
    {
        _rawLoader = rawLoader ??
            throw new ArgumentNullException(nameof(rawLoader));
        _standardLoader = standardLoader ??
            throw new ArgumentNullException(nameof(standardLoader));
    }

    public bool CanLoad(ImageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.IsRaw)
        {
            return _standardLoader.CanLoad(file);
        }

        return _rawLoader.CanLoad(file);
    }

    public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(decode);
        cancellationToken.ThrowIfCancellationRequested();
        return (file.IsRaw ? _rawLoader : _standardLoader)
            .LoadPreviewBaseWithOutcome(file, decode, cancellationToken);
    }

    public BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(decode);
        cancellationToken.ThrowIfCancellationRequested();

        var loader = file.IsRaw ? _rawLoader : _standardLoader;
        return loader.CanLoad(file)
            ? loader.LoadFullBase(file, decode, cancellationToken)
            : null;
    }
}

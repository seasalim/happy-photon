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

    public BaseImage? LoadPreviewBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        LoadPreviewBaseWithOutcome(file, decode, cancellationToken).Image;

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
        CancellationToken cancellationToken) =>
        Load(file, decode, cancellationToken, preview: false);

    private BaseImage? Load(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken,
        bool preview)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(decode);
        cancellationToken.ThrowIfCancellationRequested();

        if (!file.IsRaw)
        {
            return LoadFrom(
                _standardLoader,
                file,
                decode,
                cancellationToken,
                preview);
        }

        return LoadFrom(
            _rawLoader,
            file,
            decode,
            cancellationToken,
            preview);
    }

    private static BaseImage? LoadFrom(
        IBaseImageLoader loader,
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken,
        bool preview)
    {
        if (!loader.CanLoad(file))
        {
            return null;
        }

        return preview
            ? loader.LoadPreviewBase(file, decode, cancellationToken)
            : loader.LoadFullBase(file, decode, cancellationToken);
    }
}

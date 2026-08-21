using HappyPhoton.Models;

namespace HappyPhoton.Services;

public interface IBaseImageLoader
{
    bool CanLoad(ImageFile file);

    BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken);

    BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken);
}

public static class BaseImageLoaderExtensions
{
    public static BaseImage? LoadPreviewBase(
        this IBaseImageLoader loader,
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        loader.LoadPreviewBaseWithOutcome(file, decode, cancellationToken)
            .DetachInteractiveImage();
}

using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal sealed class NullBaseLoader : IBaseImageLoader
{
    public bool CanLoad(ImageFile file) => true;

    public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        BaseImageLoadOutcome.Failed(BaseImageLoadFailure.DecodeFailed);

    public BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) => null;
}

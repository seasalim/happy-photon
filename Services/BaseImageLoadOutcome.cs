using HappyPhoton.Models;

namespace HappyPhoton.Services;

public enum BaseImageLoadFailure
{
    None,
    SourceUnavailable,
    RawRuntimeUnavailable,
    UnsupportedRaw,
    DecodeFailed
}

public sealed record BaseImageLoadOutcome(
    PreviewBasePair? Pair,
    BaseImageLoadFailure Failure)
{
    public static BaseImageLoadOutcome Loaded(PreviewBasePair pair) =>
        new(pair, BaseImageLoadFailure.None);

    public static BaseImageLoadOutcome Loaded(BaseImage image) =>
        Loaded(new PreviewBasePair(image, large: null));

    public static BaseImageLoadOutcome Failed(BaseImageLoadFailure failure) =>
        new(null, failure);

    public static BaseImageLoadOutcome FromImage(
        BaseImage? image,
        BaseImageLoadFailure failure) =>
        image != null ? Loaded(image) : Failed(failure);

    internal BaseImage? DetachInteractiveImage()
    {
        if (Pair == null)
        {
            return null;
        }

        using var pair = Pair;
        return pair.DetachInteractive();
    }
}

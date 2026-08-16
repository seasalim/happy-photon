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
    BaseImage? Image,
    BaseImageLoadFailure Failure)
{
    public static BaseImageLoadOutcome Loaded(BaseImage image) =>
        new(image, BaseImageLoadFailure.None);

    public static BaseImageLoadOutcome Failed(BaseImageLoadFailure failure) =>
        new(null, failure);

    public static BaseImageLoadOutcome FromImage(
        BaseImage? image,
        BaseImageLoadFailure failure) =>
        image != null ? Loaded(image) : Failed(failure);
}

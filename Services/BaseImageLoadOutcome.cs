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
    internal PreviewSourceAnalysis Analysis { get; init; } =
        PreviewSourceAnalysis.Empty;

    public static BaseImageLoadOutcome Loaded(PreviewBasePair pair) =>
        new(pair, BaseImageLoadFailure.None);

    internal static BaseImageLoadOutcome Loaded(
        PreviewBasePair pair,
        PreviewSourceAnalysis analysis) =>
        new(pair, BaseImageLoadFailure.None)
        {
            Analysis = analysis
        };

    public static BaseImageLoadOutcome Loaded(BaseImage image) =>
        Loaded(new PreviewBasePair(image, large: null));

    public static BaseImageLoadOutcome Failed(BaseImageLoadFailure failure) =>
        new(null, failure);

    public static BaseImageLoadOutcome FromImage(
        BaseImage? image,
        BaseImageLoadFailure failure) =>
        image != null ? Loaded(image) : Failed(failure);

    internal static BaseImageLoadOutcome FromImage(
        BaseImage? image,
        BaseImageLoadFailure failure,
        PreviewSourceAnalysis analysis) =>
        image != null
            ? Loaded(new PreviewBasePair(image, large: null), analysis)
            : Failed(failure);

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

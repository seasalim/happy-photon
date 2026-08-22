using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;

namespace HappyPhoton.Tests;

internal sealed class CountingPairLoader : IBaseImageLoader
{
    private int _decodeCount;

    public int DecodeCount => Volatile.Read(ref _decodeCount);

    public bool CanLoad(ImageFile file) => true;

    public BaseImage? LoadPreviewBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        LoadPreviewBaseWithOutcome(
            file,
            decode,
            cancellationToken).DetachInteractiveImage();

    public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _decodeCount);
        var info = new BaseImageInfo(
            BaseSourceKind.Standard,
            false,
            decode,
            null,
            null,
            6504,
            0,
            false,
            null,
            1,
            400,
            200);
        return BaseImageLoadOutcome.Loaded(new PreviewBasePair(
            new BaseImage(
                new MagickImage(
                    MagickColors.Orange,
                    160,
                    80),
                info),
            new BaseImage(
                new MagickImage(
                    MagickColors.Orange,
                    320,
                    160),
                info)));
    }

    public BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class GatedPairLoader : IBaseImageLoader
{
    private readonly CountingPairLoader _inner = new();

    public ManualResetEventSlim DecodeStarted { get; } = new();
    public ManualResetEventSlim Release { get; } = new();
    public int DecodeCount => _inner.DecodeCount;

    public bool CanLoad(ImageFile file) => true;

    public BaseImage? LoadPreviewBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        LoadPreviewBaseWithOutcome(
            file,
            decode,
            cancellationToken).DetachInteractiveImage();

    public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken)
    {
        DecodeStarted.Set();
        Release.Wait(cancellationToken);
        return _inner.LoadPreviewBaseWithOutcome(
            file,
            decode,
            cancellationToken);
    }

    public BaseImage? LoadFullBase(
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken) =>
        _inner.LoadFullBase(file, decode, cancellationToken);
}

using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;

namespace HappyPhoton.Tests;

internal sealed class CullPerfAheadLoader(IBaseImageLoader inner) : IBaseImageLoader
{
    internal long Target { get; set; }
    internal long StartedAt;
    internal long ReturnedAt;
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool CanLoad(ImageFile file) => inner.CanLoad(file);

    public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file,
        BaseDecodeSettings decode, CancellationToken cancellationToken)
    {
        var observe = file.CatalogId == Target && Target != 0 &&
            Interlocked.CompareExchange(ref StartedAt, Stopwatch.GetTimestamp(), 0) == 0;
        if (observe)
        {
            Started.TrySetResult();
        }
        try { return inner.LoadPreviewBaseWithOutcome(file, decode, cancellationToken); }
        finally
        {
            if (observe) Interlocked.Exchange(ref ReturnedAt, Stopwatch.GetTimestamp());
        }
    }

    public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode,
        CancellationToken cancellationToken) => inner.LoadFullBase(file, decode, cancellationToken);
}

using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PreviewLoadOutcomeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-preview-outcome-{Guid.NewGuid():N}");

    [Fact]
    public async Task CancelledRequestDoesNotPublishFailure()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var loader = new BlockingLoader();
        await using var service = new PreviewService(
            catalog,
            loader,
            new RenderPipeline());
        var outcomes = new List<PreviewLoadOutcome>();
        service.PreviewLoadCompleted += (_, outcome) => outcomes.Add(outcome);
        using var cancellation = new CancellationTokenSource();

        var request = service.LoadPreviewWithHistogramAsync(
            new ImageFile(Path.Combine(_root, "cancel.dng")),
            new EditSettings(),
            skipHistogram: true,
            cancellation.Token);
        Assert.True(loader.Started.Wait(TestWaits.Condition));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        loader.Release.Set();
        // Settles before asserting an absence: a slow runner only widens the
        // window in which the cancelled outcome would have had to appear.
        await Task.Delay(50);
        Assert.Empty(outcomes);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class BlockingLoader : IBaseImageLoader
    {
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public bool CanLoad(ImageFile file) => true;

        BaseImageLoadOutcome IBaseImageLoader.LoadPreviewBaseWithOutcome(ImageFile file, BaseDecodeSettings decode, CancellationToken cancellationToken) => BaseImageLoadOutcome.FromImage(LoadPreviewBase(file, decode, cancellationToken), BaseImageLoadFailure.DecodeFailed);

        public BaseImage? LoadPreviewBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken)
        {
            Started.Set();
            Release.Wait();
            return new BaseImage(
                new MagickImage(MagickColors.Gray, 8, 8),
                new BaseImageInfo(
                    BaseSourceKind.RawLibRaw,
                    true,
                    decode,
                    null,
                    null,
                    5500,
                    0,
                    false,
                    null,
                    1,
                    8,
                    8));
        }

        public BaseImage? LoadFullBase(
            ImageFile file,
            BaseDecodeSettings decode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

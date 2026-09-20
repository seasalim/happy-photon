using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CullPerfAheadLoaderTests
{
    [Fact]
    public async Task LaterForegroundDecodeDoesNotReplaceTheFirstWarmInvocation()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var inner = new FirstCallHeldLoader(entered, release);
        var loader = new CullPerfAheadLoader(inner) { Target = 42 };
        var image = new ImageFile("target.jpg") { CatalogId = 42 };
        var warm = Task.Run(() => loader.LoadPreviewBaseWithOutcome(
            image, BaseDecodeSettings.Default, CancellationToken.None));
        try
        {
            Assert.True(entered.Wait(TestWaits.Condition));
            await loader.Started.Task.WaitAsync(TestWaits.Condition);
            var started = loader.StartedAt;
            var submitted = Stopwatch.GetTimestamp();
            loader.LoadPreviewBaseWithOutcome(image, BaseDecodeSettings.Default, CancellationToken.None);
            Assert.Equal(started, loader.StartedAt);
            Assert.Equal(0, loader.ReturnedAt);
            release.Set();
            await warm.WaitAsync(TestWaits.Condition);
            var returned = loader.ReturnedAt;
            Assert.InRange(submitted, started, returned);
            loader.LoadPreviewBaseWithOutcome(image, BaseDecodeSettings.Default, CancellationToken.None);
            Assert.Equal(started, loader.StartedAt);
            Assert.Equal(returned, loader.ReturnedAt);
        }
        finally
        {
            release.Set();
            await warm.WaitAsync(TestWaits.Condition);
        }
    }

    private sealed class FirstCallHeldLoader(ManualResetEventSlim entered, ManualResetEventSlim release)
        : IBaseImageLoader
    {
        private int _calls;
        public bool CanLoad(ImageFile file) => true;
        public BaseImageLoadOutcome LoadPreviewBaseWithOutcome(ImageFile file,
            BaseDecodeSettings decode, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                entered.Set();
                Assert.True(release.Wait(TestWaits.Condition));
            }
            return BaseImageLoadOutcome.Failed(BaseImageLoadFailure.DecodeFailed);
        }
        public BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
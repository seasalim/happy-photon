using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharpeningScratchCollection
{
    public const string Name = "Sharpening scratch ownership";
}

[Collection(SharpeningScratchCollection.Name)]
public sealed class RenderSharpeningScratchTests : IDisposable
{
    private readonly float[] _previous = RenderSharpening.TakeScratch(1);

    [Fact]
    public async Task Sharpening_ReusesScratchAcrossDifferentThreads()
    {
        var first = await OnNewThread(() => Sharpen(64, 32));
        var second = await OnNewThread(() => Sharpen(64, 32));

        Assert.NotEqual(first.ThreadId, second.ThreadId);
        Assert.Same(first.Scratch, second.Scratch);
        Assert.Equal(first.Pixels, second.Pixels);
    }

    [Fact]
    public void Sharpening_GrowsOnDemandAndReusesLargerScratch()
    {
        var small = Sharpen(32, 16);
        var large = Sharpen(128, 64);
        var smallAgain = Sharpen(32, 16);

        Assert.NotSame(small.Scratch, large.Scratch);
        Assert.Equal((64 + 2 * 2) * 128, large.Scratch.Length);
        Assert.Same(large.Scratch, smallAgain.Scratch);
        Assert.Equal(small.Pixels, smallAgain.Pixels);
    }

    [Fact]
    public async Task ConcurrentLeases_AreExclusiveAndRetainOnlyLargest()
    {
        using var acquired = new Barrier(3);
        var tasks = Enumerable.Range(1, 3).Select(size => OnNewThread(() =>
        {
            var scratch = RenderSharpening.TakeScratch(size * 1024);
            try
            {
                Array.Fill(scratch, (float)size);
                Assert.True(acquired.SignalAndWait(TestWaits.Condition));
                Assert.All(scratch, value => Assert.Equal((float)size, value));
                return scratch;
            }
            finally
            {
                RenderSharpening.ReturnScratch(scratch);
            }
        })).ToArray();
        var buffers = await Task.WhenAll(tasks);

        Assert.NotSame(buffers[0], buffers[1]);
        Assert.NotSame(buffers[0], buffers[2]);
        Assert.NotSame(buffers[1], buffers[2]);
        var retained = RenderSharpening.TakeScratch(1);
        Assert.Same(buffers[2], retained);
        Assert.DoesNotContain(RenderSharpening.TakeScratch(1), buffers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReturnScratch_KeepsLargestInEitherReturnOrder(bool largestFirst)
    {
        var small = RenderSharpening.TakeScratch(16);
        var large = RenderSharpening.TakeScratch(32);

        RenderSharpening.ReturnScratch(largestFirst ? large : small);
        RenderSharpening.ReturnScratch(largestFirst ? small : large);

        Assert.Same(large, RenderSharpening.TakeScratch(1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sharpening_ReturnsScratchAfterCancellationOrException(bool cancel)
    {
        var scratch = RenderSharpening.TakeScratch(4096);
        RenderSharpening.ReturnScratch(scratch);
        using var image = new MagickImage(MagickColors.Black, 64, 32);
        using var cancellation = new CancellationTokenSource();
        var checks = 0;
        var execution = RenderExecutionOptions.Resting(cancellation.Token,
            cancellationObserved: () =>
            {
                // The first check precedes acquisition; the second owns the scratch.
                if (++checks != 2) return;
                if (cancel) cancellation.Cancel();
                else throw new InvalidOperationException("Injected sharpening failure.");
            });
        var info = new BaseImageInfo(BaseSourceKind.RawLibRaw, true,
            BaseDecodeSettings.Default, null, null, 6504, 0, false, null, 1, 64, 32);

        var error = Record.Exception(() => RenderSharpening.ApplyCapture(
            image, info, new DetailSettings(), RenderIntent.Preview, execution: execution));

        if (cancel) Assert.IsType<OperationCanceledException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        Assert.Same(scratch, RenderSharpening.TakeScratch(1));
    }

    private static (int ThreadId, float[] Scratch, ushort[] Pixels) Sharpen(int width, int height)
    {
        using var image = new MagickImage("gradient:#182a48-#edce91",
            new MagickReadSettings { Width = (uint)width, Height = (uint)height });
        RenderSharpening.ApplyOutput(image, OutputSharpeningMode.Screen, wasResized: true);
        var scratch = RenderSharpening.TakeScratch(1);
        RenderSharpening.ReturnScratch(scratch);
        using var pixels = image.GetPixelsUnsafe();
        return (Environment.CurrentManagedThreadId, scratch,
            pixels.ToShortArray(PixelMapping.RGB)!);
    }

    private static async Task<T> OnNewThread<T>(Func<T> action) =>
        await Task.Factory.StartNew(action, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(TestWaits.Condition);

    public void Dispose()
    {
        RenderSharpening.TakeScratch(1);
        RenderSharpening.ReturnScratch(_previous);
    }
}

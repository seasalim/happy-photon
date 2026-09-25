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
    private readonly float[] _previous = RenderSharpening.Scratch.Take(1);

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sharpening_ReturnsScratchAfterCancellationOrException(bool cancel)
    {
        var scratch = RenderSharpening.Scratch.Take(4096);
        RenderSharpening.Scratch.Return(scratch);
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
        Assert.Same(scratch, RenderSharpening.Scratch.Take(1));
    }

    private static (int ThreadId, float[] Scratch, ushort[] Pixels) Sharpen(int width, int height)
    {
        using var image = new MagickImage("gradient:#182a48-#edce91",
            new MagickReadSettings { Width = (uint)width, Height = (uint)height });
        RenderSharpening.ApplyOutput(image, OutputSharpeningMode.Screen, wasResized: true);
        var scratch = RenderSharpening.Scratch.Take(1);
        RenderSharpening.Scratch.Return(scratch);
        using var pixels = image.GetPixelsUnsafe();
        return (Environment.CurrentManagedThreadId, scratch,
            pixels.ToShortArray(PixelMapping.RGB)!);
    }

    private static async Task<T> OnNewThread<T>(Func<T> action) =>
        await Task.Factory.StartNew(action, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(TestWaits.Condition);

    public void Dispose()
    {
        RenderSharpening.Scratch.Take(1);
        RenderSharpening.Scratch.Return(_previous);
    }
}

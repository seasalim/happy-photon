using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(NoiseReductionScratchCollection.Name)]
public sealed class RenderChromaScratchTests : IDisposable
{
    // The 128x96 RGB frame fits one band, so the stage's array holds every sample.
    // A shorter array would be this test's own Take(1) placeholder.
    private const int BandSamples = 128 * 96 * 3;
    private readonly ushort[] _previous = RenderChromaStage.Scratch.Take(1);

    [Fact]
    public async Task Chroma_ReusesScratchAcrossDifferentThreads()
    {
        var first = await OnNewThread(() => ApplyChroma());
        Array.Fill(first.Scratch, ushort.MaxValue);
        var second = await OnNewThread(() => ApplyChroma());

        Assert.NotEqual(first.ThreadId, second.ThreadId);
        Assert.True(first.Scratch.Length >= BandSamples);
        Assert.Same(first.Scratch, second.Scratch);
        Assert.Equal(first.Pixels, second.Pixels);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Chroma_ReturnsScratchAfterCancellationOrException(bool cancel)
    {
        var warm = ApplyChroma();
        using var cancellation = new CancellationTokenSource();
        var checks = 0;
        var execution = RenderExecutionOptions.Resting(cancellation.Token,
            cancellationObserved: () =>
            {
                // The first band check runs after acquiring the scratch array.
                if (++checks != 2) return;
                if (cancel) cancellation.Cancel();
                else throw new InvalidOperationException("Injected chroma failure.");
            });

        var error = Record.Exception(() => ApplyChroma(execution));

        if (cancel) Assert.IsType<OperationCanceledException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        var returned = RenderChromaStage.Scratch.Take(1);
        RenderChromaStage.Scratch.Return(returned);
        Assert.True(warm.Scratch.Length >= BandSamples);
        Assert.Same(warm.Scratch, returned);
        Assert.Equal(warm.Pixels, ApplyChroma().Pixels);
    }

    private static (int ThreadId, ushort[] Scratch, ushort[] Pixels) ApplyChroma(
        RenderExecutionOptions? execution = null)
    {
        using var image = new MagickImage("gradient:#182a48-#edce91",
            new MagickReadSettings { Width = 128, Height = 96 });
        Assert.True(RenderChromaStage.Apply(image,
            new EditSettings { Saturation = 20, Vibrance = 30 }, execution));
        var scratch = RenderChromaStage.Scratch.Take(1);
        RenderChromaStage.Scratch.Return(scratch);
        using var pixels = image.GetPixelsUnsafe();
        return (Environment.CurrentManagedThreadId, scratch,
            pixels.ToShortArray(PixelMapping.RGB)!);
    }

    private static async Task<T> OnNewThread<T>(Func<T> action) =>
        await Task.Factory.StartNew(action, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(TestWaits.Condition);

    public void Dispose()
    {
        RenderChromaStage.Scratch.Take(1);
        RenderChromaStage.Scratch.Return(_previous);
    }
}

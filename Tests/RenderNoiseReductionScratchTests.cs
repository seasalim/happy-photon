using HappyPhoton.Models;
using HappyPhoton.Services;
using ImageMagick;
using Xunit;

namespace HappyPhoton.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NoiseReductionScratchCollection
{
    public const string Name = "Noise reduction scratch ownership";
}

[Collection(NoiseReductionScratchCollection.Name)]
public sealed class RenderNoiseReductionScratchTests : IDisposable
{
    private readonly ScratchSet _previous = TakeScratch();

    [Theory]
    [InlineData(50, 0)]
    [InlineData(0, 50)]
    [InlineData(50, 50)]
    public async Task NoiseReduction_ReusesEveryRoleAcrossDifferentThreads(int luma, int chroma)
    {
        var first = await OnNewThread(() => Denoise(luma, chroma));
        var scratch = TakeScratch();
        Array.Fill(scratch.Source, ushort.MaxValue);
        foreach (var plane in scratch.Planes) Array.Fill(plane, float.NaN);
        ReturnScratch(scratch);
        var second = await OnNewThread(() => Denoise(luma, chroma));

        Assert.NotEqual(first.ThreadId, second.ThreadId);
        AssertActiveRolesHeld(first.Scratch, luma, chroma);
        AssertSameScratch(first.Scratch, second.Scratch);
        Assert.Equal(first.Pixels, second.Pixels);
    }

    [Theory]
    [InlineData(true, 50, 0)]
    [InlineData(false, 50, 0)]
    [InlineData(true, 0, 50)]
    [InlineData(false, 0, 50)]
    [InlineData(true, 50, 50)]
    [InlineData(false, 50, 50)]
    public void NoiseReduction_ReturnsEveryRoleAfterCancellationOrException(
        bool cancel, int luma, int chroma)
    {
        var warm = Denoise(luma, chroma);
        using var cancellation = new CancellationTokenSource();
        var checks = 0;
        var execution = RenderExecutionOptions.Resting(cancellation.Token,
            cancellationObserved: () =>
            {
                // Apply and the band check precede plane acquisition; the first
                // wavelet-scale check owns every active role.
                if (++checks != 3) return;
                if (cancel) cancellation.Cancel();
                else throw new InvalidOperationException("Injected NR failure.");
            });

        var error = Record.Exception(() => Denoise(luma, chroma, execution));

        if (cancel) Assert.IsType<OperationCanceledException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        var returned = TakeScratch();
        ReturnScratch(returned);
        AssertActiveRolesHeld(warm.Scratch, luma, chroma);
        AssertSameScratch(warm.Scratch, returned);
        Assert.Equal(warm.Pixels, Denoise(luma, chroma).Pixels);
    }

    [Fact]
    public async Task ConcurrentNoiseReductionCalls_NeverShareAnyRole()
    {
        var warm = Denoise(50, 50);
        using var acquired = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var checks = 0;
        var execution = RenderExecutionOptions.Resting(CancellationToken.None,
            cancellationObserved: () =>
            {
                if (++checks != 3) return;
                acquired.Set();
                Assert.True(release.Wait(TestWaits.Condition));
            });
        var first = OnNewThread(() => Denoise(50, 50, execution));
        try
        {
            Assert.True(acquired.Wait(TestWaits.Condition));
            // The first call holds the warmed arrays while the second completes.
            var second = await OnNewThread(() => Denoise(50, 50));
            Assert.NotSame(warm.Scratch.Source, second.Scratch.Source);
            for (var index = 0; index < warm.Scratch.Planes.Length; index++)
                Assert.NotSame(warm.Scratch.Planes[index], second.Scratch.Planes[index]);
            Assert.Equal(warm.Pixels, second.Pixels);
        }
        finally
        {
            release.Set();
            var completed = await first;
            Assert.Equal(warm.Pixels, completed.Pixels);
        }
    }

    private static (int ThreadId, ScratchSet Scratch, ushort[] Pixels) Denoise(
        int luma, int chroma, RenderExecutionOptions? execution = null)
    {
        using var image = new MagickImage("gradient:#182a48-#edce91",
            new MagickReadSettings { Width = 128, Height = 96 });
        var info = new BaseImageInfo(BaseSourceKind.RawLibRaw, true,
            BaseDecodeSettings.Default, null, null, 6504, 0, false, null, 1, 128, 96);
        RenderNoiseReduction.Apply(image, info,
            new DetailSettings { LuminanceNr = luma, ChromaNr = chroma },
            bandPixelLimit: 4096, execution: execution);
        var scratch = TakeScratch();
        ReturnScratch(scratch);
        using var pixels = image.GetPixelsUnsafe();
        return (Environment.CurrentManagedThreadId, scratch,
            pixels.ToShortArray(PixelMapping.RGB)!);
    }

    private static ScratchSet TakeScratch() => new(
        RenderNoiseReduction.SourceScratch.Take(1),
        [
            RenderNoiseReduction.LumaScratch.Take(1),
            RenderNoiseReduction.ChromaScratch.Take(1),
            RenderNoiseReduction.HorizontalScratch.Take(1),
            RenderNoiseReduction.AdjustmentScratch.Take(1)
        ]);

    private static void ReturnScratch(ScratchSet scratch)
    {
        RenderNoiseReduction.SourceScratch.Return(scratch.Source);
        RenderNoiseReduction.LumaScratch.Return(scratch.Planes[0]);
        RenderNoiseReduction.ChromaScratch.Return(scratch.Planes[1]);
        RenderNoiseReduction.HorizontalScratch.Return(scratch.Planes[2]);
        RenderNoiseReduction.AdjustmentScratch.Return(scratch.Planes[3]);
    }

    // A one-element array can only be this test's own Take(1) placeholder, so every
    // role the stage used must hold a band-sized array for identity checks to count.
    private static void AssertActiveRolesHeld(ScratchSet scratch, int luma, int chroma)
    {
        Assert.True(scratch.Source.Length > 1);
        if (luma > 0) Assert.True(scratch.Planes[0].Length > 1);
        if (chroma > 0) Assert.True(scratch.Planes[1].Length > 1);
        Assert.True(scratch.Planes[2].Length > 1);
        Assert.True(scratch.Planes[3].Length > 1);
    }

    private static void AssertSameScratch(ScratchSet expected, ScratchSet actual)
    {
        Assert.Same(expected.Source, actual.Source);
        for (var index = 0; index < expected.Planes.Length; index++)
            Assert.Same(expected.Planes[index], actual.Planes[index]);
    }

    private static async Task<T> OnNewThread<T>(Func<T> action) =>
        await Task.Factory.StartNew(action, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(TestWaits.Condition);

    public void Dispose()
    {
        TakeScratch();
        ReturnScratch(_previous);
    }

    private sealed record ScratchSet(ushort[] Source, float[][] Planes);
}

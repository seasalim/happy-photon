using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class RenderScratchSlotTests
{
    private readonly RenderScratchSlot<float> _slot = new();

    [Fact]
    public async Task Scratch_ReusesAcrossDifferentThreads()
    {
        var first = await OnNewThread(() =>
        {
            var scratch = _slot.Take(16);
            _slot.Return(scratch);
            return (ThreadId: Environment.CurrentManagedThreadId, Scratch: scratch);
        });
        var second = await OnNewThread(() =>
            (ThreadId: Environment.CurrentManagedThreadId, Scratch: _slot.Take(16)));

        Assert.NotEqual(first.ThreadId, second.ThreadId);
        Assert.Same(first.Scratch, second.Scratch);
    }

    [Fact]
    public void Scratch_GrowsOnDemandAndReusesLargerArray()
    {
        var small = _slot.Take(16);
        _slot.Return(small);
        var large = _slot.Take(32);
        _slot.Return(large);

        Assert.NotSame(small, large);
        Assert.Equal(32, large.Length);
        Assert.Same(large, _slot.Take(16));
    }

    [Fact]
    public async Task ConcurrentLeases_AreExclusiveAndRetainOnlyLargest()
    {
        using var acquired = new Barrier(3);
        var tasks = Enumerable.Range(1, 3).Select(size => OnNewThread(() =>
        {
            var scratch = _slot.Take(size * 1024);
            try
            {
                Array.Fill(scratch, (float)size);
                Assert.True(acquired.SignalAndWait(TestWaits.Condition));
                Assert.All(scratch, value => Assert.Equal((float)size, value));
                return scratch;
            }
            finally
            {
                _slot.Return(scratch);
            }
        })).ToArray();
        var buffers = await Task.WhenAll(tasks);

        Assert.NotSame(buffers[0], buffers[1]);
        Assert.NotSame(buffers[0], buffers[2]);
        Assert.NotSame(buffers[1], buffers[2]);
        var retained = _slot.Take(1);
        Assert.Same(buffers[2], retained);
        Assert.DoesNotContain(_slot.Take(1), buffers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReturnScratch_KeepsLargestInEitherReturnOrder(bool largestFirst)
    {
        var small = _slot.Take(16);
        var large = _slot.Take(32);

        _slot.Return(largestFirst ? large : small);
        _slot.Return(largestFirst ? small : large);

        Assert.Same(large, _slot.Take(1));
    }

    private static async Task<T> OnNewThread<T>(Func<T> action) =>
        await Task.Factory.StartNew(action, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).WaitAsync(TestWaits.Condition);
}

using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DebouncedActionTests
{
    [Fact]
    public async Task RunAsync_CancelledDelayDoesNotRunAction()
    {
        using var cancellation = new CancellationTokenSource();
        var ran = false;

        var task = DebouncedAction.RunAsync(
            "test",
            TimeSpan.FromSeconds(5),
            cancellation.Token,
            () =>
            {
                ran = true;
                return Task.CompletedTask;
            });
        cancellation.Cancel();

        await task;

        Assert.False(ran);
    }

    [Fact]
    public async Task RunAsync_ReportsAndSwallowsActionFailure()
    {
        string? operation = null;
        Exception? reported = null;

        await DebouncedAction.RunAsync(
            "autosave",
            TimeSpan.Zero,
            CancellationToken.None,
            () => throw new InvalidOperationException("boom"),
            (name, ex) =>
            {
                operation = name;
                reported = ex;
            });

        Assert.Equal("autosave", operation);
        Assert.IsType<InvalidOperationException>(reported);
    }
}

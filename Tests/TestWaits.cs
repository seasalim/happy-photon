using System.Runtime.CompilerServices;
using Xunit;

namespace HappyPhoton.Tests;

/// <summary>
/// Every timeout in the test tree bounds a hang; none of them assert latency.
/// CI runners stall far past what a dev machine suggests, so waits are held in
/// one place and kept generous rather than tuned per call site.
/// </summary>
internal static class TestWaits
{
    /// <summary>The ceiling for any wait on a signal or an observed condition.</summary>
    public static readonly TimeSpan Condition = TimeSpan.FromSeconds(30);

    /// <summary>Polls until the condition holds, failing with its source text.</summary>
    public static async Task UntilAsync(
        Func<bool> condition,
        [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        var deadline = DateTime.UtcNow + Condition;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, Timeout(expression));
            await Task.Delay(10);
        }
    }

    /// <summary>The blocking form, for tests that own the dispatcher thread.</summary>
    public static void Until(
        Func<bool> condition,
        [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        var deadline = DateTime.UtcNow + Condition;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }
        Assert.True(condition(), Timeout(expression));
    }

    private static string Timeout(string? expression) =>
        $"Condition '{expression}' was still false after " +
        $"{Condition.TotalSeconds:0}s.";
}

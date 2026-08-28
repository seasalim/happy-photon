using Avalonia;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class BeforeAfterSplitHeadlessTests
{
    private static async Task AssertEventuallyCloseAsync(
        ZoomPanControl expected,
        ZoomPanControl actual)
    {
        await TestWaits.UntilAsync(() =>
            expected.VisibleRegion is { } expectedRegion &&
            actual.VisibleRegion is { } actualRegion &&
            AreClose(expectedRegion.Center, actualRegion.Center));
        AssertClose(expected.VisibleRegion!.Value.Center, actual.VisibleRegion!.Value.Center);
    }

    private static bool AreClose(Point expected, Point actual) =>
        Math.Abs(expected.X - actual.X) <= 0.01 &&
        Math.Abs(expected.Y - actual.Y) <= 0.01;

    private static void AssertClose(Point expected, Point actual)
    {
        Assert.InRange(Math.Abs(expected.X - actual.X), 0, 0.01);
        Assert.InRange(Math.Abs(expected.Y - actual.Y), 0, 0.01);
    }
}

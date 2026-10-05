using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ControlBarLayoutSettlingTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task WidthSweepAndAssessmentChangesSettle(double scaling)
    {
        await ControlBarGateScene.WithScene((window, vm) =>
        {
            window.SetRenderScaling(scaling);
            window.MinWidth = 0;

            foreach (var develop in new[] { false, true })
            {
                ControlBarGateScene.Mode(window, vm, develop ? "develop" : "browse-grid");
                var bar = ControlBarGateScene.Bar(window, develop);
                var layout = bar.GetVisualDescendants().OfType<ControlBarLayout>().Single();
                var unsettled = 0;

                foreach (var step in Enumerable.Range(0, 151))
                {
                    window.Width = 800 + step * 7.3;
                    SettleInTwoCycles(window, layout);
                    unsettled += LateInvalidCycles(window, layout) > 0 ? 1 : 0;
                    AssertClusterGeometry(layout);
                }

                window.Width = 1900;
                SettleInTwoCycles(window, layout);

                foreach (var flag in Enum.GetValues<ImageFlag>())
                {
                    vm.SelectedImage!.Flag = flag;
                    SettleInTwoCycles(window, layout);
                    Assert.Equal(0, LateInvalidCycles(window, layout));
                }

                foreach (var rating in Enumerable.Range(0, 6))
                {
                    vm.SelectedImage!.Rating = rating;
                    SettleInTwoCycles(window, layout);
                    Assert.Equal(0, LateInvalidCycles(window, layout));
                }

                foreach (var label in Enum.GetValues<ColorLabel>())
                {
                    vm.SelectedImage!.ColorLabel = label;
                    SettleInTwoCycles(window, layout);
                    Assert.Equal(0, LateInvalidCycles(window, layout));
                }

                output.WriteLine($"WP5 G2 scale={scaling} develop={develop} unsettled={unsettled}/151; flag/rating/color late=0");
                Assert.Equal(0, unsettled);
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FixedWidthScalingTransitionsReprepareCluster(bool develop)
    {
        await ControlBarGateScene.WithScene((window, vm) =>
        {
            window.MinWidth = 0;
            window.Width = 1160;
            ControlBarGateScene.Mode(window, vm, develop ? "develop" : "browse-grid");
            var bar = ControlBarGateScene.Bar(window, develop);
            var layout = bar.GetVisualDescendants().OfType<ControlBarLayout>().Single();

            foreach (var scaling in new[] { 1.0, 1.25, 1.5, 2.0, 1.0 })
            {
                window.SetRenderScaling(scaling);
                SettleInTwoCycles(window, layout);
                Assert.Equal(1160, window.Width);
                Assert.Equal(0, LateInvalidCycles(window, layout));
                AssertClusterGeometry(layout);
            }

            return Task.CompletedTask;
        });
    }

    internal static void Cycle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void SettleInTwoCycles(Window window, ControlBarLayout layout)
    {
        Cycle(window);
        Cycle(window);
        Assert.True(layout.IsMeasureValid && layout.IsArrangeValid,
            $"Bar did not settle in two cycles at window width {window.Width}.");
    }

    internal static int LateInvalidCycles(Window window, ControlBarLayout layout)
    {
        for (var cycle = 0; cycle < 6; cycle++)
        {
            Cycle(window);
        }

        var invalid = 0;

        for (var cycle = 0; cycle < 6; cycle++)
        {
            if (!layout.IsMeasureValid || !layout.IsArrangeValid) invalid++;
            Cycle(window);
        }

        return invalid;
    }

    private static void AssertClusterGeometry(ControlBarLayout layout)
    {
        var left = layout.Children[0];
        var slot = (Grid)layout.Children[1];
        var right = layout.Children[2];
        left.Measure(Size.Infinity);
        right.Measure(Size.Infinity);
        var minimumLeft = left.MinWidth > 0 ? left.MinWidth : left.DesiredSize.Width;
        var room = layout.Bounds.Width - minimumLeft - right.DesiredSize.Width;
        var fullExpected = room >= ControlBarLayout.FullClusterWidth;
        var compactExpected = !fullExpected && room >= slot.Children[1].Width;
        Assert.Equal(fullExpected, slot.Children[0].IsVisible);
        Assert.Equal(compactExpected, slot.Children[1].IsVisible);
        var cluster = slot.Children.SingleOrDefault(child => child.IsVisible);
        if (cluster is null) return;

        var bounds = ControlBarGateScene.Bounds(cluster, layout);
        var expectedX = Math.Clamp((layout.Bounds.Width - cluster.Width) / 2,
            left.Bounds.Right, Math.Max(left.Bounds.Right, right.Bounds.Left - cluster.Width));
        Assert.InRange(Math.Abs(bounds.Left - expectedX), 0, 1);
        Assert.InRange(Math.Abs(bounds.Width - cluster.Width), 0, 1);
    }
}

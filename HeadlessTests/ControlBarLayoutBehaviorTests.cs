using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ControlBarLayoutBehaviorTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task DevelopHidesSliderBeforeClusterLeavesCentre()
    {
        await ControlBarGateScene.WithScene(async (window, vm) =>
        {
            ControlBarGateScene.Mode(window, vm, "develop");
            var bar = ControlBarGateScene.Bar(window, true);
            var slider = ControlBarGateScene.Named<CompactSlider>(bar, "DevelopZoomSlider");
            var full = ControlBarGateScene.Full(bar);
            var sawSlider = false;
            var sawNoSliderCentred = false;

            foreach (var width in Enumerable.Range(0, 81).Select(step => 1400 - step * 10))
            {
                ControlBarGateScene.ViewerWidth(window, bar, width);
                if (!full.IsEffectivelyVisible) continue;
                var offset = ControlBarGateScene.Offset(full, bar);
                output.WriteLine($"WP12 G4 Develop order width={width} slider={slider.IsVisible} offset={offset:F3}");

                if (slider.IsVisible)
                {
                    sawSlider = true;
                    Assert.True(offset <= 1, $"Slider remains shown with full cluster {offset:F3}px off centre at viewer {width}.");
                }
                else if (offset <= 1)
                {
                    sawNoSliderCentred = true;
                }
            }

            Assert.True(sawSlider && sawNoSliderCentred, "Missing slider-shown and slider-hidden centred tiers.");
            await Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task DevelopHasMinimallyShiftedFullClusterWithSliderHidden()
    {
        await ControlBarGateScene.WithScene(async (window, vm) =>
        {
            ControlBarGateScene.Mode(window, vm, "develop");
            var bar = ControlBarGateScene.Bar(window, true);
            var slider = ControlBarGateScene.Named<CompactSlider>(bar, "DevelopZoomSlider");
            var full = ControlBarGateScene.Full(bar);
            var left = ControlBarGateScene.Named<StackPanel>(bar, "DevelopImageActionsPanel");
            var right = ControlBarGateScene.Named<StackPanel>(bar, "DevelopViewStatePanel");
            var sawOffCentre = false;

            foreach (var width in Enumerable.Range(0, 81).Select(step => 1400 - step * 10))
            {
                ControlBarGateScene.ViewerWidth(window, bar, width);
                if (!full.IsEffectivelyVisible || slider.IsVisible) continue;
                var cluster = ControlBarGateScene.Bounds(full, bar);
                if (ControlBarGateScene.Offset(full, bar) <= 1) continue;
                var leftBounds = ControlBarGateScene.Bounds(left, bar);
                var rightBounds = ControlBarGateScene.Bounds(right, bar);
                var low = leftBounds.Right + cluster.Width / 2;
                var high = rightBounds.Left - cluster.Width / 2;
                Assert.True(low <= high, "Full cluster cannot fit between side groups.");
                var expectedCentre = Math.Clamp(bar.Bounds.Width / 2, low, high);
                Assert.True(Math.Abs(cluster.Center.X - expectedCentre) <= 1,
                    $"Cluster centre {cluster.Center.X:F3} differs from minimum shift {expectedCentre:F3} at {width}.");
                Assert.False(cluster.Intersects(leftBounds));
                Assert.False(cluster.Intersects(rightBounds));
                sawOffCentre = true;
            }

            Assert.True(sawOffCentre, "Missing full-cluster off-centre tier with the slider hidden.");
            await Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData("full")]
    [InlineData("compact")]
    [InlineData("empty")]
    public async Task BrowseHasFullCompactAndEmptySteps(string required)
    {
        await ControlBarGateScene.WithScene(async (window, vm) =>
        {
            ControlBarGateScene.Mode(window, vm, "browse-grid");
            var bar = ControlBarGateScene.Bar(window, false);
            var full = ControlBarGateScene.Full(bar);
            var found = false;

            foreach (var width in Enumerable.Range(0, 111).Select(step => 1400 - step * 10))
            {
                ControlBarGateScene.ViewerWidth(window, bar, width);
                var compact = bar.GetVisualDescendants().OfType<DevelopAssessmentCluster>()
                    .SingleOrDefault(cluster => cluster.IsEffectivelyVisible);
                var mode = full.IsEffectivelyVisible ? "full" : compact != null ? "compact" : "empty";

                if (mode != required) continue;
                found = true;
                var visible = mode == "full" ? (Control)full : compact;

                if (visible != null)
                {
                    var bounds = ControlBarGateScene.Bounds(visible, bar);
                    var right = ControlBarGateScene.Named<StackPanel>(bar, "ThumbnailSizePanel");
                    Assert.True(new Rect(bar.Bounds.Size).Contains(bounds));
                    Assert.False(bounds.Intersects(ControlBarGateScene.Bounds(right, bar)));
                }

                output.WriteLine($"WP12 G4 Browse {required} observed at viewer={width}");
                break;
            }

            Assert.True(found, $"Missing Browse {required} assessment step in viewer widths 1400..300.");
            await Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task BrowseFooterDoesNotOverlapAt800By500()
    {
        await ControlBarGateScene.WithScene(async (window, vm) =>
        {
            ControlBarGateScene.Mode(window, vm, "browse-grid");
            window.Width = 800;
            window.Height = 500;
            ControlBarGateScene.Settle(window);
            var bar = ControlBarGateScene.Bar(window, false);
            var full = ControlBarGateScene.Full(bar);
            var right = ControlBarGateScene.Named<StackPanel>(bar, "ThumbnailSizePanel");
            var cluster = full.IsEffectivelyVisible ? (Control)full
                : bar.GetVisualDescendants().OfType<DevelopAssessmentCluster>()
                    .SingleOrDefault(control => control.IsEffectivelyVisible);

            if (cluster != null)
            {
                var bounds = ControlBarGateScene.Bounds(cluster, bar);
                var rightBounds = ControlBarGateScene.Bounds(right, bar);
                var overlap = Math.Max(0, Math.Min(bounds.Right, rightBounds.Right) - Math.Max(bounds.Left, rightBounds.Left));
                output.WriteLine($"WP12 G4 Browse 800x500 viewer={bar.Bounds.Width:F3} overlap={overlap:F3}");
                Assert.True(overlap <= 0, $"Assessment cluster overlaps right group by {overlap:F3}px.");
                Assert.True(new Rect(bar.Bounds.Size).Contains(bounds));
            }

            Assert.True(new Rect(bar.Bounds.Size).Contains(ControlBarGateScene.Bounds(right, bar)));
            await Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssessmentStateKeepsClusterAndNeighboursFixed(bool develop)
    {
        await ControlBarGateScene.WithScene(async (window, vm) =>
        {
            ControlBarGateScene.Mode(window, vm, develop ? "develop" : "browse-grid");
            var bar = ControlBarGateScene.Bar(window, develop);
            ControlBarGateScene.ViewerWidth(window, bar, 1400);
            var full = ControlBarGateScene.Full(bar);
            var neighbours = bar.GetVisualDescendants().OfType<Control>()
                .Where(control => control is Button or CompactSlider)
                .Where(control => !control.GetVisualAncestors().Contains(full))
                .Where(control => control.IsEffectivelyVisible).ToArray();
            var clusterBounds = ControlBarGateScene.Bounds(full, bar);
            var neighbourBounds = neighbours.Select(control => ControlBarGateScene.Bounds(control, bar)).ToArray();
            var states = 0;
            var clusterDelta = 0.0;
            var neighbourDelta = 0.0;

            foreach (var flag in Enum.GetValues<ImageFlag>())
            foreach (var rating in Enumerable.Range(0, 6))
            foreach (var label in Enum.GetValues<ColorLabel>())
            {
                vm.SelectedImage!.Flag = flag;
                vm.SelectedImage.Rating = rating;
                vm.SelectedImage.ColorLabel = label;
                ControlBarGateScene.Settle(window);
                clusterDelta = Math.Max(clusterDelta, Difference(clusterBounds, ControlBarGateScene.Bounds(full, bar)));

                for (var index = 0; index < neighbours.Length; index++)
                {
                    neighbourDelta = Math.Max(neighbourDelta,
                        Difference(neighbourBounds[index], ControlBarGateScene.Bounds(neighbours[index], bar)));
                }

                states++;
            }

            output.WriteLine($"WP12 G4 {(develop ? "Develop" : "Browse")} steadiness states={states} neighbours={neighbours.Length} clusterDelta={clusterDelta:F3} neighbourDelta={neighbourDelta:F3}");
            Assert.Equal(0, clusterDelta);
            Assert.Equal(0, neighbourDelta);
            await Task.CompletedTask;
        });
    }

    private static double Difference(Rect before, Rect after) =>
        new[] { Math.Abs(before.X - after.X), Math.Abs(before.Y - after.Y),
            Math.Abs(before.Width - after.Width), Math.Abs(before.Height - after.Height) }.Max();
}

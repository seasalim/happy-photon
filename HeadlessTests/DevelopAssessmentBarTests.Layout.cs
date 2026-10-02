using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class DevelopAssessmentBarTests
{
    [AvaloniaFact]
    public async Task FullHostFitsTheWidestFlagState()
    {
        await WithWindow(async (window, vm, _) =>
        {
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            var host = pane.FindControl<Border>("DevelopAssessmentHost")!;
            var full = pane.FindControl<ImageAssessmentControl>("DevelopImageAssessment")!;
            var widest = 0.0;

            foreach (var flag in Enum.GetValues<ImageFlag>())
            {
                vm.SelectedImage!.Flag = flag;
                Settle(window);
                full.Measure(Size.Infinity);
                widest = Math.Max(widest, full.DesiredSize.Width);
            }

            Assert.Equal(Math.Ceiling(widest), host.Width);
            await Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task WidthTiersKeepNeighboursFixedAcrossAllStatesAndResize()
    {
        await WithWindow(async (window, vm, _) =>
        {
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            var host = pane.FindControl<Border>("DevelopAssessmentHost")!;
            var cluster = pane.FindControl<DevelopAssessmentCluster>("DevelopCompactAssessment")!;
            var actions = pane.FindControl<StackPanel>("DevelopImageActionsPanel")!;
            var views = pane.FindControl<StackPanel>("DevelopViewStatePanel")!;
            var slot = pane.FindControl<Grid>("DevelopAssessmentSlot")!;

            var footer = window.FindControl<BrowseGridView>("BrowseGridView")!
                .FindControl<ImageAssessmentControl>("ImageAssessment")!;
            var controls = views.GetVisualDescendants().OfType<Button>()
                .Prepend(pane.FindControl<Button>("RotateLeftButton")!).ToArray();
            var emptyWidth = actions.Bounds.Width + views.Bounds.Width + 20;

            foreach (var width in new[] { emptyWidth + host.Width + 1, emptyWidth + cluster.Width + 1, emptyWidth })
            {
                pane.Width = width;
                Settle(window);
                var bounds = controls.Select(control => BoundsIn(control, pane)).ToArray();
                var fullVisible = width > emptyWidth + host.Width;
                var compactVisible = !fullVisible && width > emptyWidth;
                AssertMode(pane, fullVisible, compactVisible);

                foreach (var flag in Enum.GetValues<ImageFlag>())
                foreach (var rating in Enumerable.Range(0, 6))
                foreach (var label in Enum.GetValues<ColorLabel>())
                {
                    vm.SelectedImage!.Flag = flag;
                    vm.SelectedImage.Rating = rating;
                    vm.SelectedImage.ColorLabel = label;
                    Settle(window);

                    AssertMode(pane, fullVisible, compactVisible);
                    AssertState(pane, footer, vm.SelectedImage);
                    Assert.Equal(bounds, controls.Select(control => BoundsIn(control, pane)).ToArray());

                    if (fullVisible || compactVisible)
                    {
                        var visible = fullVisible ? (Control)host : cluster;
                        var contentBounds = BoundsIn(visible, pane);
                        Assert.True(BoundsIn(slot, pane).Contains(contentBounds));
                        Assert.False(contentBounds.Intersects(BoundsIn(actions, pane)));
                        Assert.False(contentBounds.Intersects(BoundsIn(views, pane)));
                        Assert.True(BoundsIn(pane.FindControl<Border>("DevelopControlBar")!, pane).Contains(contentBounds));
                    }

                    if (fullVisible)
                    {
                        var full = pane.FindControl<ImageAssessmentControl>("DevelopImageAssessment")!;
                        Assert.True(full.DesiredSize.Width <= host.Width,
                            $"Full control needs {full.DesiredSize.Width} px; host has {host.Width} px.");
                    }
                }
            }

            foreach (var width in new[] { 1300, 800, 600, 1300 })
            {
                pane.Width = width;
                Settle(window);
                AssertMode(pane, width == 1300, width == 800);
            }

            // The overflow button leaves enough space for the compact assessment cluster.
            pane.ClearValue(Control.WidthProperty);
            window.Width = 800;
            Settle(window);
            AssertMode(pane, false, true);

            await Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColumnResizingAlsoSelectsWidthTier(bool compact)
    {
        await WithWindow(async (window, _, _) =>
        {
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            var workspace = pane.GetVisualAncestors().OfType<Grid>()
                .Single(grid => grid.ColumnDefinitions.Count == 5);

            // Pane maximums (400/450) bound the viewer, so use a window where they still reach each tier.
            window.Width = 1500;
            Settle(window);
            AssertMode(pane, true, false);

            workspace.ColumnDefinitions[0].Width = new GridLength(400);
            workspace.ColumnDefinitions[4].Width = new GridLength(compact ? 250 : 450);
            Settle(window);
            AssertMode(pane, false, compact);

            await Task.CompletedTask;
        });
    }

    private static Rect BoundsIn(Control control, Control parent) =>
        new(control.TranslatePoint(default, parent)!.Value, control.Bounds.Size);

    private static void AssertMode(DevelopViewerPane pane, bool full, bool compact)
    {
        Assert.Equal(full, pane.FindControl<Border>("DevelopAssessmentHost")!.IsVisible);
        Assert.Equal(compact, pane.FindControl<DevelopAssessmentCluster>("DevelopCompactAssessment")!.IsVisible);
    }
}

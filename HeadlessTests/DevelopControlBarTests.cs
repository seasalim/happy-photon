using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class DevelopControlBarTests
{
    [AvaloniaTheory]
    [InlineData(1692)]
    [InlineData(800)]
    public async Task TiersFitAtBoundariesAndReevaluateSlotAfterResizing(int initialWidth)
    {
        await WithWindow(initialWidth, (window, vm) =>
        {
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            AssertTier(pane, initialWidth == 800 ? "overflow" : "full");
            Resize(window, pane, 1200);
            var (full, noSlider) = Thresholds(pane);
            // WP7: five 24px icons, a 180px slider, the 9px separator, ten
            // 8px gaps, 20px bar padding, and filled text labels with 6px padding.
            var face = new Avalonia.Media.Typeface(
                ThemeResourceTests.Resource<Avalonia.Media.FontFamily>("FontBody", Avalonia.Styling.ThemeVariant.Dark),
                weight: Avalonia.Media.FontWeight.SemiBold);
            var labels = new[] { "Fit", "1:1", "Assess", "Y|Y", "J|R" };
            var labelWidth = labels.Sum(text => Math.Ceiling(new Avalonia.Media.TextFormatting.TextLayout(
                text, face, 10, Avalonia.Media.Brushes.White).WidthIncludingTrailingWhitespace) + 12);
            var expected = 5 * 24 + 180 + 9 + 10 * 8 + 20 + labelWidth;
            Assert.Equal(ControlBarLayout.FullClusterWidth + 2 * (expected - 20 - 137) + 20, full);
            Assert.Equal(expected - 180 - 8, noSlider);

            foreach (var width in new[] { 1200, 500, 308, 1200,
                         full + 1, full, full - 1, noSlider + 1, noSlider, noSlider - 1 })
            {
                Resize(window, pane, width);
                AssertTier(pane, width >= full ? "full" : width >= noSlider ? "no-slider" : "overflow");
                AssertGeometry(pane);
                AssertSlot(pane);
            }

            // At widths below the overflow group's floor, only edge clipping is permitted.
            pane.Width = 0;
            Settle(window);
            AssertGeometry(pane, allowClipping: true);

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task StateChangesCannotMoveThresholdsOrVisibleButtons()
    {
        await WithWindow(1692, (window, vm) =>
        {
            var pane = window.FindControl<DevelopViewerPane>("DevelopViewerPane")!;
            var (full, noSlider) = Thresholds(pane);
            Assert.True(vm.CanSwitchCaptureMember);

            foreach (var width in new[] { full + 1, noSlider + 1 })
            {
                Resize(window, pane, width);
                var tier = width > full ? "full" : "no-slider";
                var buttons = pane.FindControl<Border>("DevelopControlBar")!
                    .GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToArray();
                var bounds = buttons.Select(button => BoundsIn(button, pane)).ToArray();
                Action[] changes =
                [
                    () => vm.ToggleActualSizeCommand.Execute(null),
                    () => vm.ZoomFitCommand!.Execute(null),
                    () => vm.ManualZoomLevel = 5,
                    () => vm.ToggleColorAssessmentModeCommand.Execute(null),
                    () => vm.ToggleColorAssessmentModeCommand.Execute(null),
                    () => vm.ToggleBeforeAfterSplitCommand.Execute(null),
                    () => vm.ToggleBeforeAfterSplitCommand.Execute(null),
                    () => vm.SwitchCaptureMemberCommand.Execute(null),
                    () => vm.SwitchCaptureMemberCommand.Execute(null),
                    () => vm.SelectedImage!.Flag = ImageFlag.Picked,
                    () => vm.SelectedImage!.Flag = ImageFlag.Rejected,
                    () => vm.SelectedImage!.Flag = ImageFlag.Unflagged,
                    () => vm.IsCropMode = true,
                    () => vm.IsCropMode = false
                ];

                foreach (var change in changes)
                {
                    change();
                    Settle(window);
                    AssertTier(pane, tier);
                    Assert.Equal(bounds, buttons.Select(button => BoundsIn(button, pane)).ToArray());
                }

                Resize(window, pane, width - 2);
                AssertTier(pane, width > full ? "no-slider" : "overflow");
            }

            return Task.CompletedTask;
        });
    }

    private static (double Full, double NoSlider) Thresholds(DevelopViewerPane pane)
    {
        var bar = pane.FindControl<Border>("DevelopControlBar")!;
        var left = pane.FindControl<StackPanel>("DevelopImageActionsPanel")!;
        var right = pane.FindControl<StackPanel>("DevelopViewStatePanel")!;
        var slider = pane.FindControl<CompactSlider>("DevelopZoomSlider")!;
        var full = left.Bounds.Width + right.Bounds.Width + bar.Padding.Left + bar.Padding.Right;

        return (ControlBarLayout.FullClusterWidth + 2 * Math.Max(left.Bounds.Width, right.Bounds.Width) +
            bar.Padding.Left + bar.Padding.Right, full - slider.Bounds.Width - right.Spacing);
    }

    private static void AssertTier(DevelopViewerPane pane, string tier)
    {
        var right = pane.FindControl<StackPanel>("DevelopViewStatePanel")!;
        Assert.Equal(tier == "full", pane.FindControl<CompactSlider>("DevelopZoomSlider")!.IsVisible);
        Assert.Equal(tier == "overflow", pane.FindControl<Button>("DevelopViewActionsButton")!.IsVisible);

        foreach (var button in right.Children.OfType<Button>().Where(button => button.Name != "DevelopViewActionsButton"))
        {
            Assert.Equal(tier != "overflow", button.IsVisible);
        }
    }

    private static void AssertGeometry(DevelopViewerPane pane, bool allowClipping = false)
    {
        var bar = pane.FindControl<Border>("DevelopControlBar")!;
        var right = pane.FindControl<StackPanel>("DevelopViewStatePanel")!;
        Control[] controls = [pane.FindControl<StackPanel>("DevelopImageActionsPanel")!,
            pane.FindControl<Border>("DevelopAssessmentHost")!,
            pane.FindControl<DevelopAssessmentCluster>("DevelopCompactAssessment")!,
            .. right.Children];
        var visible = controls.Where(control => control.IsEffectivelyVisible).ToArray();

        for (var i = 0; i < visible.Length; i++)
        {
            var bounds = BoundsIn(visible[i], bar);

            if (!allowClipping)
            {
                Assert.True(new Rect(bar.Bounds.Size).Contains(bounds), $"{visible[i].Name} outside {bar.Bounds}: {bounds}");
            }

            foreach (var other in visible.Skip(i + 1))
            {
                Assert.False(bounds.Intersects(BoundsIn(other, bar)), $"{visible[i].Name} overlaps {other.Name}");
            }
        }

        if (!allowClipping)
        {
            var slot = BoundsIn(pane.FindControl<Grid>("DevelopAssessmentSlot")!, bar);
            Assert.True(new Rect(bar.Bounds.Size).Contains(slot));
            Assert.False(slot.Intersects(BoundsIn(controls[0], bar)));
            Assert.False(slot.Intersects(BoundsIn(right, bar)));
        }
    }

    private static void AssertSlot(DevelopViewerPane pane)
    {
        var slot = pane.FindControl<Grid>("DevelopAssessmentSlot")!;
        var host = pane.FindControl<Border>("DevelopAssessmentHost")!;
        var cluster = pane.FindControl<DevelopAssessmentCluster>("DevelopCompactAssessment")!;
        Assert.Equal(slot.Bounds.Width >= host.Width, host.IsVisible);
        Assert.Equal(!host.IsVisible && slot.Bounds.Width >= cluster.Width, cluster.IsVisible);
    }

    private static void Resize(MainWindow window, DevelopViewerPane pane, double viewerWidth)
    {
        window.Width += viewerWidth - pane.Bounds.Width;
        Settle(window);
        Assert.Equal(viewerWidth, pane.Bounds.Width, 5);
    }

    private static Rect BoundsIn(Control control, Control parent) =>
        new(control.TranslatePoint(default, parent)!.Value, control.Bounds.Size);

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WithWindow(int width, Func<MainWindow, MainWindowViewModel, Task> assertion)
    {
        using var fixture = new CatalogVmFixture("develop-control-bar");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(catalog, loadMetadataAsync: _ => Task.CompletedTask);
        var images = new[] { "capture.jpg", "capture.dng", "other.jpg" }
            .Select(name => new ImageFile(fixture.Path(name))).ToArray();

        foreach (var image in images)
        {
            image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        }

        vm.Browse.SetImages(images);
        vm.RestoreShowCapturePairs(true);
        vm.SelectedImage = images[0];
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        var window = new MainWindow { Width = width, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Settle(window);

        await assertion(window, vm);
    }
}

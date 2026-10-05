using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ControlBarTooltipTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public async Task PinnedInventoryOpensAboveTargetsWithoutInvalidatingLayout(double scaling)
    {
        await ControlBarGateScene.WithScene((window, vm) =>
        {
            window.SetRenderScaling(scaling);
            var area = window.Screens.Primary!.WorkingArea;
            window.Position = new PixelPoint(area.X, area.Bottom - (int)(window.Height * scaling));
            vm.SelectedImage!.ColorLabel = ColorLabel.Red;

            foreach (var develop in new[] { false, true })
            {
                window.Width = 1900;
                ControlBarGateScene.Mode(window, vm, develop ? "develop" : "browse-grid");
                var bar = ControlBarGateScene.Bar(window, develop);
                var buttons = ControlBarTooltipInventory.Full(bar, develop);

                foreach (var button in buttons)
                {
                    AssertTooltip(window, bar, button);
                }

                output.WriteLine($"WP5 G1/G3/G4 scale={scaling} develop={develop} opened={buttons.Length}; pointer=0; button=0; late=0");
                ControlBarGateScene.ViewerWidth(window, bar, develop ? 300 : 500);
                var compact = bar.GetVisualDescendants().OfType<DevelopAssessmentCluster>().Single();
                Assert.True(compact.IsEffectivelyVisible);
                AssertTooltip(window, bar, ControlBarGateScene.Named<Border>(compact, "CompactColorLabel"));

                if (develop)
                {
                    AssertTooltip(window, bar, ControlBarGateScene.Named<Button>(bar, "DevelopViewActionsButton"));
                }
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public async Task HoverStaysQuietAcrossFractionalWidthSweep(double scaling)
    {
        await ControlBarGateScene.WithScene((window, vm) =>
        {
            window.SetRenderScaling(scaling);
            window.MinWidth = 0;

            foreach (var develop in new[] { false, true })
            {
                ControlBarGateScene.Mode(window, vm, develop ? "develop" : "browse-grid");
                var bar = ControlBarGateScene.Bar(window, develop);
                var button = ControlBarGateScene.Named<Button>(bar, "NextImageButton");

                foreach (var step in Enumerable.Range(0, 151))
                {
                    window.Width = 800 + step * 7.3;
                    ControlBarLayoutSettlingTests.Cycle(window);
                    AssertTooltip(window, bar, button);
                }

                output.WriteLine($"WP5 G4 scale={scaling} develop={develop} hover late=0 at all 151 widths");
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task RenderFitTooltipScene()
    {
        await ControlBarGateScene.WithScene((window, vm) =>
        {
            using var theme = new TestUiScope(theme: ThemeVariant.Dark);
            window.Width = 1200;
            window.Height = 700;
            ControlBarGateScene.Mode(window, vm, "develop");
            var bar = ControlBarGateScene.Bar(window, true);
            var fit = ControlBarGateScene.Named<Button>(bar, "ZoomFitButton");
            window.MouseMove(ControlBarGateScene.Bounds(fit, window).Center);
            ToolTip.SetIsOpen(fit, true);

            try
            {
                ControlBarGateScene.Settle(window);
                var tip = window.GetVisualDescendants().OfType<ToolTip>().Single();
                ShowcaseTestHelper.Settle(() => tip.Opacity == 1, "Fit tooltip fade-in");
                Assert.True(fit.IsPointerOver);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(new PixelSize(1200, 700), frame.PixelSize);
                var directory = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots");
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, "develop-bar-tooltip-fit.png"), PngBitmapEncoderOptions.Default);
            }
            finally
            {
                ToolTip.SetIsOpen(fit, false);
            }

            return Task.CompletedTask;
        });
    }

    private static void AssertTooltip(MainWindow window, Control bar, Control target)
    {
        Assert.Equal(PlacementMode.Top, ToolTip.GetPlacement(target));
        Assert.Equal(0, ToolTip.GetVerticalOffset(target));
        Assert.NotNull(ToolTip.GetTip(target));
        Assert.True(target.IsEffectivelyVisible);
        var pointer = ControlBarGateScene.Bounds(target, window).Center;
        window.MouseMove(pointer);
        ToolTip.SetIsOpen(target, true);

        try
        {
            ControlBarLayoutSettlingTests.Cycle(window);
            var tip = window.GetVisualDescendants().OfType<ToolTip>().Single();
            Assert.True(tip.IsEffectivelyVisible);
            Assert.True(tip.Bounds.Width > 0 && tip.Bounds.Height > 0);
            var tipBounds = ScreenBounds(tip);
            var targetBounds = ScreenBounds(target);
            Assert.False(tipBounds.Contains(window.PointToScreen(pointer).ToPoint(1)));
            Assert.False(tipBounds.Intersects(targetBounds));
            var layout = bar.GetVisualDescendants().OfType<ControlBarLayout>().Single();
            Assert.Equal(0, ControlBarLayoutSettlingTests.LateInvalidCycles(window, layout));
        }
        finally
        {
            ToolTip.SetIsOpen(target, false);
            ControlBarLayoutSettlingTests.Cycle(window);
        }
    }

    private static Rect ScreenBounds(Control control)
    {
        var origin = control.PointToScreen(default).ToPoint(1);
        var end = control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height)).ToPoint(1);

        return new Rect(origin, end);
    }
}

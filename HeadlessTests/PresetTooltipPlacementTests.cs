using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PresetTooltipPlacementTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public async Task BuiltInLookTipsOpenBesideTheListAtEveryPaneLimit(double scaling)
    {
        await ControlBarGateScene.WithScene((window, vm) =>
        {
            window.SetRenderScaling(scaling);
            var grid = (Grid)ControlBarGateScene.Named<Border>(window, "WorkspaceLeftPanel").Parent!;
            var panel = ControlBarGateScene.Named<PresetsPanel>(window, "PresetsPanel");
            ExpandAll(window, panel);

            foreach (var layout in new[] { "default", "minimum window", "widest left pane" })
            {
                Layout(window, grid, layout);
                var buttons = BuiltInLooks(vm, panel);

                foreach (var button in buttons)
                {
                    AssertTooltip(window, panel, button);
                }

                output.WriteLine($"scale={scaling} layout={layout} window={window.Bounds.Width:F0} left={grid.ColumnDefinitions[0].ActualWidth:F0} opened={buttons.Length}");
            }

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task RenderPresetTooltipScene()
    {
        await ControlBarGateScene.WithScene((window, vm) =>
        {
            using var theme = new TestUiScope(theme: ThemeVariant.Dark);
            window.Width = 1200;
            window.Height = 700;
            ControlBarGateScene.Settle(window);
            var panel = ControlBarGateScene.Named<PresetsPanel>(window, "PresetsPanel");
            var button = BuiltInLooks(vm, panel).First();
            window.MouseMove(ControlBarGateScene.Bounds(button, window).Center);
            ToolTip.SetIsOpen(button, true);

            try
            {
                ControlBarGateScene.Settle(window);
                var tip = window.GetVisualDescendants().OfType<ToolTip>().Single();
                ShowcaseTestHelper.Settle(() => tip.Opacity == 1, "Preset tooltip fade-in");
                Assert.True(button.IsPointerOver);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.Equal(new PixelSize(1200, 700), frame.PixelSize);
                var directory = Path.Combine(GoldenTestPaths.RepositoryRoot, "artifacts", "shots");
                Directory.CreateDirectory(directory);
                frame.Save(Path.Combine(directory, "develop-preset-tooltip-right.png"), PngBitmapEncoderOptions.Default);
            }
            finally
            {
                ToolTip.SetIsOpen(button, false);
            }

            return Task.CompletedTask;
        });
    }

    private static void ExpandAll(MainWindow window, PresetsPanel panel)
    {
        foreach (var expander in panel.GetVisualDescendants().OfType<Expander>())
        {
            expander.IsExpanded = true;
        }

        ControlBarGateScene.Settle(window);
    }

    private static void Layout(MainWindow window, Grid grid, string layout)
    {
        grid.ColumnDefinitions[0].Width = new GridLength(240);
        window.Width = layout == "minimum window" ? window.MinWidth : 1440;
        ControlBarGateScene.Settle(window);

        if (layout == "widest left pane")
        {
            grid.ColumnDefinitions[0].Width = new GridLength(grid.ColumnDefinitions[0].MaxWidth);
            ControlBarGateScene.Settle(window);
            Assert.Equal(400, grid.ColumnDefinitions[0].ActualWidth, precision: 3);
        }

        if (layout == "minimum window")
        {
            Assert.Equal(800, window.Bounds.Width, precision: 3);
        }
    }

    private static Button[] BuiltInLooks(MainWindowViewModel vm, PresetsPanel panel)
    {
        var buttons = panel.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("preset") && button.Tag is string id
                && vm.PresetService.GetById(id)?.IsBuiltIn == true)
            .ToArray();
        Assert.Equal(vm.PresetService.BuiltInPresets.Count, buttons.Length);

        return buttons;
    }

    private static void AssertTooltip(MainWindow window, PresetsPanel panel, Button button)
    {
        Assert.Equal(PlacementMode.Right, ToolTip.GetPlacement(button));
        Assert.Equal(4, ToolTip.GetHorizontalOffset(button));
        Assert.Equal(0, ToolTip.GetVerticalOffset(button));
        Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(button) as string));
        button.BringIntoView();
        ControlBarGateScene.Settle(window);
        var pointer = ControlBarGateScene.Bounds(button, window).Center;
        window.MouseMove(pointer);
        ToolTip.SetIsOpen(button, true);

        try
        {
            ControlBarGateScene.Settle(window);
            Assert.True(button.IsPointerOver);
            var tip = window.GetVisualDescendants().OfType<ToolTip>().Single();
            Assert.True(tip.IsEffectivelyVisible);
            Assert.True(tip.Bounds.Width > 0 && tip.Bounds.Height > 0);
            var tipBounds = ScreenBounds(tip);
            var rowBounds = ScreenBounds(button);
            var viewport = ScreenBounds(panel.GetVisualDescendants().OfType<ScrollViewer>().Single());
            Assert.False(tipBounds.Contains(window.PointToScreen(pointer).ToPoint(1)));
            Assert.False(tipBounds.Intersects(viewport));
            Assert.True(tipBounds.Left >= viewport.Right, $"Tip {tipBounds} starts left of the list {viewport}.");
            Assert.True(tipBounds.Top <= rowBounds.Center.Y && tipBounds.Bottom >= rowBounds.Center.Y,
                $"Tip {tipBounds} is not level with its row {rowBounds}.");
        }
        finally
        {
            ToolTip.SetIsOpen(button, false);
            ControlBarGateScene.Settle(window);
        }
    }

    private static Rect ScreenBounds(Control control)
    {
        var origin = control.PointToScreen(default).ToPoint(1);
        var end = control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height)).ToPoint(1);

        return new Rect(origin, end);
    }
}

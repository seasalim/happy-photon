using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class DevelopToolsLayoutTests
{
    public static IEnumerable<object[]> Frames =>
        from mode in new[] { "normal", "crop", "locals", "radial" }
        from gray in new[] { false, true }
        from size in new[] { new PixelSize(1200, 700), new PixelSize(800, 500) }
        select new object[] { mode, gray, size.Width, size.Height };

    [AvaloniaTheory]
    [MemberData(nameof(Frames))]
    public async Task FixedToolsRemainReachable(string mode, bool gray, int width, int height)
    {
        await DevelopToolsBaselineTests.WithScene(mode, width, height, async (vm, scope) =>
        {
            using var theme = new TestUiScope(theme: gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
            // test-teardown-policy: allow - WithScene owns and disposes this MainWindow scope.
            scope.Show();
            Dispatcher.UIThread.RunJobs();
            // Enter tools after the window's initial focus/layout, as a user does.
            if (mode == "crop")
            {
                await vm.ToggleCropModeCommand.ExecuteAsync(null);
                await vm.ToggleCropModeCommand.ExecuteAsync(null);
            }
            if (mode is "locals" or "radial")
            {
                await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
                await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
            }
            Dispatcher.UIThread.RunJobs();
            var window = scope.Window!;
            using var frame = window.CaptureRenderedFrame();
            Assert.Equal(new PixelSize(width, height), frame!.PixelSize);
            var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
            var viewer = window.GetVisualDescendants().OfType<DevelopViewerPane>().Single();
            var row = panel.FindControl<StackPanel>("DevelopToolRow")!;
            Inside(row, panel);
            var crop = panel.FindControl<ToggleButton>("CropModeButton")!;
            var locals = panel.FindControl<ToggleButton>("LocalsModeButton")!;
            Assert.Equal(crop.Bounds.Width, locals.Bounds.Width);
            AssertTool(crop, vm.IsCropMode, "Crop", "contains a committed crop");
            AssertTool(locals, vm.IsLocalsMode, "Locals", "contains saved locals");
            foreach (var name in new[] { "CropModeButton", "LocalsModeButton" })
                Assert.Single(window.GetLogicalDescendants().OfType<ToggleButton>(), b => b.Name == name);
            Assert.DoesNotContain(viewer.GetLogicalDescendants().OfType<Control>(), c =>
                c is CropEditSection or LocalsEditSection || c is CompactSlider { Label: "Horizon" } ||
                c is ToggleButton && ToolTip.GetTip(c)?.ToString() == "Lock Aspect Ratio" ||
                c is Button b && (b.Command == vm.ToggleCropModeCommand || b.Command == vm.ToggleLocalsModeCommand ||
                    b.Command == vm.ApplyCropCommand || b.Command == vm.CancelCropCommand || b.Command == vm.ResetCropCommand));
            var scroll = panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(scroll.Viewport.Height > 0);
            var cropHeader = panel.FindControl<Grid>("CropToolHeader")!;
            var localsHeader = panel.FindControl<Grid>("LocalsToolHeader")!;
            Assert.Equal(vm.IsCropMode, cropHeader.IsVisible);
            Assert.Equal(vm.IsLocalsMode, localsHeader.IsVisible);
            if (mode != "normal")
            {
                var section = panel.GetVisualDescendants().OfType<Control>().Single(c =>
                    mode == "crop" ? c is CropEditSection : c is LocalsEditSection);
                Assert.Equal(0, scroll.Offset.Y);
                var first = section.GetVisualDescendants().OfType<Control>().First(c =>
                    mode == "crop" ? c is CompactSlider : c is ListBox);
                Inside(first, scroll);
                var header = mode == "crop" ? cropHeader : localsHeader;
                Inside(header, panel);
                scroll.Offset = new Vector(0, scroll.Extent.Height);
                Dispatcher.UIThread.RunJobs();
                foreach (var button in header.GetVisualDescendants().OfType<Button>())
                {
                    Inside(button, panel);
                    var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    var hit = window.InputHitTest(center) as Visual;
                    Assert.True(ReferenceEquals(hit, button) || hit?.GetVisualAncestors().Contains(button) == true);
                    Assert.True(button.Focus());
                    Assert.True(button.IsFocused);
                }
            }
            await Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task LocalColorTracksAndRowGapsMatchTheSection()
    {
        await DevelopToolsBaselineTests.WithScene("locals", 1200, 700, async (_, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns and disposes this MainWindow scope.
            scope.Show();
            Dispatcher.UIThread.RunJobs();
            var section = scope.Window!.GetVisualDescendants().OfType<LocalsEditSection>().Single();
            var rows = section.GetVisualDescendants().OfType<CompactSlider>().Take(4).ToArray();
            Assert.Equal(new[] { "Exposure", "Temperature", "Tint", "Saturation" }, rows.Select(s => s.Label));
            var gaps = rows.Zip(rows.Skip(1), (a, b) =>
                b.TranslatePoint(default, section)!.Value.Y - a.TranslatePoint(default, section)!.Value.Y - a.Bounds.Height).ToArray();
            Assert.All(gaps, gap => Assert.Equal(8, gap));
            string[][] tokens = [["WhiteBalanceCoolColor", "WhiteBalanceNeutralColor", "WhiteBalanceWarmColor"],
                ["WhiteBalanceTintGreenColor", "WhiteBalanceTintNeutralColor", "WhiteBalanceTintMagentaColor"]];
            for (var i = 0; i < 2; i++)
            {
                var slider = rows[i + 1];
                var brush = Assert.IsType<LinearGradientBrush>(slider.TrackBrush);
                Assert.False(slider.ShowValueFill);
                Assert.Equal(new RelativePoint(0, .5, RelativeUnit.Relative), brush.StartPoint);
                Assert.Equal(new RelativePoint(1, .5, RelativeUnit.Relative), brush.EndPoint);
                Assert.Equal(new[] { 0d, .5, 1 }, brush.GradientStops.Select(s => s.Offset));
                Assert.Equal(tokens[i].Select(t => ThemeResourceTests.Resource<Color>(t, ThemeVariant.Dark)),
                    brush.GradientStops.Select(s => s.Color));
            }
            await Task.CompletedTask;
        });
    }

    private static void AssertTool(ToggleButton button, bool active, string name, string description)
    {
        Assert.Equal(active, button.IsChecked);
        var peer = new ToggleButtonAutomationPeer(button);
        Assert.Equal(name, peer.GetName());
        Assert.Equal(description, AutomationProperties.GetHelpText(button));
        Assert.Equal(active ? ToggleState.On : ToggleState.Off, ((IToggleProvider)peer).ToggleState);
        var dot = button.GetVisualDescendants().OfType<Ellipse>().Single();
        Assert.True(dot.IsVisible);
        Assert.Equal(1, dot.Opacity);
        var theme = button.ActualThemeVariant;
        Assert.Equal(ThemeResourceTests.Resource<SolidColorBrush>(active ? "ControlActive" : "ControlHover", theme).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color);
        Assert.Equal(ThemeResourceTests.Resource<SolidColorBrush>(active ? "OnControlActive" : "ControlActive", theme).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(button.Foreground).Color);
        Assert.Equal(ThemeResourceTests.Resource<SolidColorBrush>(active ? "OnControlActive" : "ControlActive", theme).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(dot.Fill).Color);
    }

    private static void Inside(Control child, Control parent)
    {
        var origin = child.TranslatePoint(default, parent)!.Value;
        Assert.True(child.Bounds.Width > 0 && child.Bounds.Height > 0);
        Assert.True(origin.X >= 0 && origin.Y >= 0 &&
            origin.X + child.Bounds.Width <= parent.Bounds.Width + .01 &&
            origin.Y + child.Bounds.Height <= parent.Bounds.Height + .01,
            $"{child.Name ?? child.GetType().Name}: {child.Bounds} at {origin} outside {parent.Bounds}");
    }
}

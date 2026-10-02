using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AutoStraightenViewTests
{
    [AvaloniaTheory]
    [InlineData("develop-crop-auto", false, 250)]
    [InlineData("develop-crop-auto-midgray", true, 250)]
    [InlineData("develop-crop-auto-narrow", false, 200)]
    public async Task RenderCropAutoScene(string scene, bool gray, int paneWidth)
    {
        await DevelopToolsBaselineTests.WithScene("crop", 1440, 900, (_, scope) =>
        {
            ShowcaseTestHelper.Capture(scene, scope, new PixelSize(1440, 900),
                gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark, window =>
                {
                    SetPaneWidth(window, paneWidth);
                    var section = window.GetVisualDescendants().OfType<CropEditSection>().Single();
                    Assert.True(section.IsEffectivelyVisible);
                    Assert.True(section.FindControl<Button>("AutoStraightenButton")!.IsEffectivelyVisible);
                    ShowcaseTestHelper.SettleExpanderChevrons(window);
                });

            return Task.CompletedTask;
        });
    }

    [AvaloniaTheory]
    [InlineData(250, false)]
    [InlineData(250, true)]
    [InlineData(200, false)]
    [InlineData(200, true)]
    public async Task AutoFitsWithoutChangingHorizonTrack(int paneWidth, bool gray)
    {
        await DevelopToolsBaselineTests.WithScene("crop", 1440, 900, (_, scope) =>
        {
            using var theme = new TestUiScope(theme: gray ? HappyPhotonThemes.MidGray : ThemeVariant.Dark);
            // test-teardown-policy: allow - WithScene owns and disposes this MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            SetPaneWidth(window, paneWidth);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
            var section = panel.GetVisualDescendants().OfType<CropEditSection>().Single();
            var auto = section.FindControl<Button>("AutoStraightenButton")!;
            var slider = section.GetVisualDescendants().OfType<CompactSlider>().Single();
            Assert.Equal(paneWidth == 250 ? 110 : 60,
                slider.FindControl<Grid>("LayoutGrid")!.ColumnDefinitions[1].ActualWidth);
            Assert.Equal(24, auto.Bounds.Height);
            Assert.Contains("compact-button", auto.Classes);
            Assert.True(auto.IsEffectivelyVisible);
            var bounds = BoundsIn(auto, section);
            Assert.True(new Rect(section.Bounds.Size).Contains(bounds));
            var scroll = panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!;
            Assert.True(new Rect(scroll.Viewport).Contains(BoundsIn(auto, scroll)));
            Assert.All(section.GetVisualDescendants().OfType<Button>().Where(button => button != auto),
                button => Assert.False(BoundsIn(button, section).Intersects(bounds)));

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task EnterAfterClickingAutoAppliesCrop()
    {
        await DevelopToolsBaselineTests.WithScene("crop", 1440, 900, async (vm, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns and disposes this MainWindow scope.
            scope.Show();
            var window = scope.Window!;
            SetPaneWidth(window, 250);
            var auto = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "AutoStraightenButton");
            Assert.True(auto.Focus());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var point = auto.TranslatePoint(new Point(auto.Bounds.Width / 2, auto.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            await TestWaits.UntilAsync(() => vm.AutoStraightenCommand.ExecutionTask != null);
            await vm.AutoStraightenCommand.ExecutionTask!.WaitAsync(TestWaits.Condition);
            Assert.False(auto.IsFocused);
            Assert.True(vm.IsCropMode);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await TestWaits.UntilAsync(() => !vm.IsCropMode);

            if (vm.ApplyCropCommand.ExecutionTask is { } apply)
            {
                await apply.WaitAsync(TestWaits.Condition);
            }
        });
    }

    private static Rect BoundsIn(Control control, Control parent) =>
        new(control.TranslatePoint(default, parent)!.Value, control.Bounds.Size);

    private static void SetPaneWidth(Window window, int width)
    {
        Dispatcher.UIThread.RunJobs();
        var panel = window.GetVisualDescendants().OfType<DevelopEditPanel>().Single();
        panel.GetVisualAncestors().OfType<Grid>().Single(grid => grid.ColumnDefinitions.Count == 5)
            .ColumnDefinitions[4].Width = new GridLength(width);
        panel.FindControl<ScrollViewer>("DevelopControlsScrollViewer")!.Offset = default;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}



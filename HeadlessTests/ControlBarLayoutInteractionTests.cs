using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class ControlBarLayoutInteractionTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowseNavigationScrollsAndReanchorsShiftSelection(bool previous)
    {
        await ControlBarGateScene.WithScene((window, vm) =>
        {
            ControlBarGateScene.Mode(window, vm, "browse-grid");
            window.Width = 800;
            window.Height = 500;
            ControlBarGateScene.Settle(window);
            var grid = ControlBarGateScene.Named<BrowseGridView>(window, "BrowseGridView");
            var scroll = ControlBarGateScene.Named<ScrollViewer>(grid, "ThumbnailScrollViewer");
            var images = vm.Browse.VisibleImages.ToArray();
            var start = previous ? 2 : 0;
            vm.SelectedImage = images[start];
            grid.SelectionAnchor = images[start];
            grid.ScrollItemIntoView(start);
            ControlBarGateScene.Settle(window);
            var initialOffset = scroll.Offset.Y;
            var bar = ControlBarGateScene.Bar(window, false);
            var button = ControlBarGateScene.Named<Button>(bar,
                previous ? "PreviousImageButton" : "NextImageButton");
            Click(window, button);
            Click(window, button);
            var target = previous ? 0 : 2;
            Assert.Same(images[target], vm.SelectedImage);
            output.WriteLine($"Browse {(previous ? "previous" : "next")}: scroll {initialOffset} -> {scroll.Offset.Y}");
            Assert.True(previous ? scroll.Offset.Y < initialOffset : scroll.Offset.Y > initialOffset,
                $"Scroll offset stayed at {scroll.Offset.Y} after navigation from {initialOffset}.");
            Assert.Same(images[target], grid.SelectionAnchor);
            var tile = grid.GetVisualDescendants().OfType<Border>().Single(control =>
                control.Name == "ThumbnailTile" && ReferenceEquals(control.DataContext, images[target]));
            var tileBounds = ControlBarGateScene.Bounds(tile, scroll);
            Assert.InRange(tileBounds.Top, 0, scroll.Viewport.Height - tileBounds.Height);
            Assert.True(grid.Focus());
            var key = previous ? Key.Right : Key.Left;
            window.KeyPress(key, RawInputModifiers.Shift, PhysicalKey.None, null);
            window.KeyRelease(key, RawInputModifiers.Shift, PhysicalKey.None, null);
            ControlBarGateScene.Settle(window);
            Assert.Equal(previous ? images[..2] : images[1..], vm.Browse.GetSelectedImages());

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task BrowseNavigationMatchesDevelopAndMessageYieldsBeforeClusterMoves()
    {
        await ControlBarGateScene.WithScene((window, vm) =>
        {
            ControlBarGateScene.Mode(window, vm, "browse-grid");
            var browse = ControlBarGateScene.Bar(window, false);
            var develop = ControlBarGateScene.Bar(window, true);

            foreach (var name in new[] { "PreviousImageButton", "NextImageButton" })
            {
                var button = ControlBarGateScene.Named<Button>(browse, name);
                var counterpart = ControlBarGateScene.Named<Button>(develop, name);
                Assert.Same(counterpart.Command, button.Command);
                Assert.Equal(ToolTip.GetTip(counterpart), ToolTip.GetTip(button));
                Assert.Equal(AutomationProperties.GetName(counterpart), AutomationProperties.GetName(button));
                Assert.Contains("icon-button", button.Classes);
            }

            var message = ControlBarGateScene.Named<TextBlock>(browse, "OnlineOnlyMessage");
            message.Text = new string('W', 100);
            message.IsVisible = true;
            ControlBarGateScene.ViewerWidth(window, browse, 1400);
            var wideMessage = message.Bounds.Width;
            ControlBarGateScene.ViewerWidth(window, browse, 850);
            Assert.True(message.Bounds.Width < wideMessage);
            var cluster = ControlBarGateScene.Full(browse);
            Assert.True(cluster.IsEffectivelyVisible);
            Assert.InRange(ControlBarGateScene.Offset(cluster, browse), 0, 1);
            Assert.False(ControlBarGateScene.Bounds(message, browse)
                .Intersects(ControlBarGateScene.Bounds(cluster, browse)));

            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task HiddenSliderKeepsZoomKeysWheelFitAndActualSize()
    {
        await ControlBarGateScene.WithScene((window, vm) =>
        {
            ControlBarGateScene.Mode(window, vm, "develop");
            var bar = ControlBarGateScene.Bar(window, true);
            ControlBarGateScene.ViewerWidth(window, bar, 1000);
            Assert.False(ControlBarGateScene.Named<CompactSlider>(bar, "DevelopZoomSlider").IsVisible);
            var pane = ControlBarGateScene.Named<DevelopViewerPane>(window, "DevelopViewerPane");
            pane.Focus();

            foreach (var key in new[] { Key.Space, Key.Z })
            {
                var fit = vm.IsZoomFitMode;
                window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
                Assert.Equal(!fit, vm.IsZoomFitMode);
            }

            Click(window, ControlBarGateScene.Named<Button>(bar, "ActualSizeButton"));
            Assert.False(vm.IsZoomFitMode);
            Click(window, ControlBarGateScene.Named<Button>(bar, "ZoomFitButton"));
            Assert.True(vm.IsZoomFitMode);
            var priorZoom = vm.ZoomLevel;
            window.MouseWheel(pane.Viewer.TranslatePoint(new Point(100, 100), window)!.Value,
                new Vector(0, 1), RawInputModifiers.None);
            ControlBarGateScene.Settle(window);
            Assert.True(vm.ZoomLevel > priorZoom);

            return Task.CompletedTask;
        });
    }

    private static void Click(Window window, Button button)
    {
        Assert.True(button.IsEffectivelyVisible);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        ControlBarGateScene.Settle(window);
    }
}

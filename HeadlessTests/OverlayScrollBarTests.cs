using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace HappyPhoton.Tests;

public sealed class OverlayScrollBarTests
{
    [AvaloniaFact]
    public async Task TallHistoryRevealsAndDragsOverlayScrollBar()
    {
        var (window, _, scroll) = ShowHistory(40);

        try
        {
            await AssertOverlayRevealAndHalfDragAsync(window, scroll, scroll);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShortHistoryDoesNotShowScrollBar()
    {
        var (window, _, scroll) = ShowHistory(2);

        try
        {
            Assert.False(VerticalScrollBar(scroll).IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OverlayClassIsDefinedCentrallyAndAppliedToTwoHosts()
    {
        var tree = new FolderTreePanel().FindControl<TreeView>("FolderTree")!;
        var history = new EditHistoryPanel().FindControl<ScrollViewer>(
            "HistoryScrollViewer")!;

        Assert.All(new Control[] { tree, history }, host =>
            Assert.Contains("overlay-scrollbar", host.Classes));

        var root = GoldenTestPaths.RepositoryRoot;
        var references = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(IsProductionSource)
            .Where(path => File.ReadAllText(path).Contains(
                "overlay-scrollbar", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order()
            .ToArray();

        Assert.Equal(
        [
            "App.axaml",
            "Views/EditHistoryPanel.axaml",
            "Views/FolderTreePanel.axaml"
        ], references);
        var app = File.ReadAllText(Path.Combine(root, "App.axaml"));
        Assert.Contains("ScrollViewer.overlay-scrollbar", app);
        Assert.Contains("TreeView.overlay-scrollbar", app);
        Assert.DoesNotContain("folder-tree-overlay-scrollbar", app);
    }

    private static async Task AssertOverlayRevealAndHalfDragAsync(
        Window window, Control hoverTarget, ScrollViewer scroll)
    {
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var scrollBar = VerticalScrollBar(scroll);
        var thumb = Assert.Single(
            scrollBar.GetVisualDescendants().OfType<Thumb>());

        Assert.True(scrollBar.IsVisible);
        Assert.Equal(0, thumb.Opacity);
        Assert.Equal(
            scrollBar.TranslatePoint(default, window)!.Value.Y,
            thumb.TranslatePoint(default, window)!.Value.Y);

        var hoverPoint = hoverTarget.TranslatePoint(
            new Point(hoverTarget.Bounds.Width / 2, hoverTarget.Bounds.Height / 2),
            window)!.Value;
        window.MouseMove(hoverPoint, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.True(scroll.IsPointerOver,
            $"Pointer missed ScrollViewer at {hoverPoint} with bounds {scroll.Bounds}.");
        await TestWaits.UntilAsync(() => thumb.Opacity == 1);

        Assert.Equal(6, thumb.Bounds.Width);
        var thumbPoint = thumb.TranslatePoint(
            new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2),
            window)!.Value;
        window.MouseMove(thumbPoint, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await TestWaits.UntilAsync(() => scrollBar.IsExpanded);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(12, scrollBar.Bounds.Width);
        Assert.Equal(10, thumb.Bounds.Width);
        Assert.Null(Assert.Single(
            scrollBar.GetVisualDescendants().OfType<Grid>(),
            candidate => candidate.Name == "Root").Background);
        Assert.Equal(0, Assert.Single(
            scrollBar.GetVisualDescendants().OfType<Rectangle>(),
            candidate => candidate.Name == "TrackRect").Opacity);
        var lineButtons = scrollBar.GetVisualDescendants().OfType<RepeatButton>()
            .Where(button => button.Name is
                "PART_LineUpButton" or "PART_LineDownButton")
            .ToArray();
        Assert.Equal(2, lineButtons.Length);
        Assert.All(
            lineButtons,
            button => Assert.False(button.IsVisible));

        thumbPoint = thumb.TranslatePoint(
            new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2),
            window)!.Value;
        var maximum = scroll.ScrollBarMaximum.Y;
        var track = Assert.Single(
            scrollBar.GetVisualDescendants().OfType<Track>());
        var freeTrack = track.Bounds.Height - thumb.Bounds.Height;
        var end = thumbPoint + new Vector(0, freeTrack * .5);
        window.MouseDown(thumbPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.InRange(scroll.Offset.Y, maximum * .4, maximum * .6);
    }

    private static (Window Window, EditHistoryPanel Panel, ScrollViewer Scroll)
        ShowHistory(int entryCount)
    {
        var panel = new EditHistoryPanel { Width = 240, Height = 300 };
        var window = new Window { Width = 240, Height = 300, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var items = Assert.Single(
            panel.GetVisualDescendants().OfType<ItemsControl>());
        items.ItemsSource = Enumerable.Range(0, entryCount)
            .Select(index => new EditHistoryEntry(
                index,
                index == 0 ? "Original" : $"Exposure +{index / 100d:0.00}",
                new EditSettings { Exposure = index / 100d }))
            .ToArray();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (window, panel,
            panel.FindControl<ScrollViewer>("HistoryScrollViewer")!);
    }

    private static ScrollBar VerticalScrollBar(ScrollViewer scroll) =>
        Assert.Single(
            scroll.GetVisualDescendants().OfType<ScrollBar>(),
            candidate => candidate.Orientation == Avalonia.Layout.Orientation.Vertical);

    private static bool IsProductionSource(string path)
    {
        var relative = Path.GetRelativePath(
            GoldenTestPaths.RepositoryRoot, path).Replace('\\', '/');
        if (relative.Split('/').Any(part => part is
                "artifacts" or "bin" or "obj" or "HeadlessTests" or "Tests"))
        {
            return false;
        }

        return Path.GetExtension(path) is ".axaml" or ".cs";
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class TipsLayoutTests
{
    [AvaloniaTheory]
    [InlineData(WorkspaceMode.Browse, 800, 500)]
    [InlineData(WorkspaceMode.Develop, 800, 500)]
    [InlineData(WorkspaceMode.Export, 800, 500)]
    [InlineData(WorkspaceMode.Browse, 1200, 700)]
    [InlineData(WorkspaceMode.Develop, 1200, 700)]
    [InlineData(WorkspaceMode.Export, 1200, 700)]
    public async Task G8_CardAndActionsFitImageArea(WorkspaceMode mode, int width, int height)
    {
        await using var scene = new TipsTestScene(width, height);
        scene.Vm.WorkspaceMode = mode;
        scene.Vm.IsExportJobRunning = mode == WorkspaceMode.Export;
        var card = TipsTestScene.Card(scene.Window, mode);
        scene.Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var bounds = BoundsInWindow(card, scene.Window);
        var area = mode switch
        {
            WorkspaceMode.Browse => Named(scene.Window, "ThumbnailScrollViewer"),
            WorkspaceMode.Develop => Named(scene.Window, "ViewerGrid"),
            _ => Named(scene.Window, "ExportPreviewPane")
        };
        Assert.True(BoundsInWindow(area, scene.Window).Contains(bounds));
        Assert.InRange(bounds.Width, 1, 300);

        var emptyName = mode switch
        {
            WorkspaceMode.Browse => "EmptyState",
            WorkspaceMode.Develop => "DevelopEmptyState",
            _ => "ExportPreviewEmptyState"
        };
        var empty = Named(scene.Window, emptyName);
        var emptyBounds = BoundsInWindow(empty, scene.Window);
        Assert.True(empty.IsEffectivelyVisible);
        Assert.False(bounds.Intersects(emptyBounds), $"{emptyName} {emptyBounds} intersects tips {bounds}");
        Assert.True(BoundsInWindow(area, scene.Window).Contains(emptyBounds));

        foreach (var text in empty.GetVisualDescendants().OfType<TextBlock>())
        {
            Assert.True(emptyBounds.Contains(BoundsInWindow(text, scene.Window)), text.Text);
        }

        foreach (var button in empty.GetVisualDescendants().OfType<Button>())
        {
            var actionBounds = BoundsInWindow(button, scene.Window);
            Assert.True(emptyBounds.Contains(actionBounds));
            var hit = scene.Window.InputHitTest(actionBounds.Center) as Visual;
            Assert.True(ReferenceEquals(hit, button) || hit?.GetVisualAncestors().Contains(button) == true);
        }

        var chromeNames = mode switch
        {
            WorkspaceMode.Browse => new[] { "BrowseFooterSurface" },
            WorkspaceMode.Develop => ["DevelopControlBar"],
            _ => ["ExportProofToggle", "ExportProofSizeChooser"]
        };

        foreach (var name in chromeNames)
        {
            var chrome = Named(scene.Window, name);

            if (chrome.IsEffectivelyVisible)
            {
                Assert.False(bounds.Intersects(BoundsInWindow(chrome, scene.Window)), name);
            }
        }

        foreach (var strip in scene.Window.GetVisualDescendants().OfType<ExportQueueStrip>())
        {
            if (strip.IsEffectivelyVisible && strip.Bounds.Height > 0)
            {
                Assert.False(bounds.Intersects(BoundsInWindow(strip, scene.Window)));
            }
        }

        foreach (var text in new[] { "Don't show tips", "Got it", "Keyboard shortcuts" })
        {
            var button = TipsTestScene.Action(card, text);
            var actionBounds = BoundsInWindow(button, scene.Window);
            Assert.True(bounds.Contains(actionBounds));
            var hit = scene.Window.InputHitTest(actionBounds.Center) as Visual;
            Assert.True(ReferenceEquals(hit, button) || hit?.GetVisualAncestors().Contains(button) == true,
                $"{mode} {text} at {actionBounds}: hit {hit}");
        }
    }

    internal static Control Named(Control root, string name)
    {
        var control = root.GetVisualDescendants().OfType<Control>().SingleOrDefault(control => control.Name == name);
        Assert.True(control != null, $"Missing {name}; names: {string.Join(", ", root.GetVisualDescendants().OfType<Control>().Select(item => item.Name).Where(item => item != null))}");

        return control!;
    }

    internal static Rect BoundsInWindow(Control control, Window window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
}

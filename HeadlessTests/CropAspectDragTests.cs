using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CropAspectDragTests
{
    [AvaloniaTheory]
    [InlineData("TopLeft", -1, 0)]
    [InlineData("TopLeft", 0, -1)]
    [InlineData("TopCenter", 0, -1)]
    [InlineData("MiddleRight", 1, 0)]
    public void ApproximatePresetAtMinimumKeepsExactRatioInsideBounds(string handle, double dx, double dy)
    {
        var start = new CropRegion { Left = .1, Top = 0, Right = .15, Bottom = .05 / .753 };
        Assert.Equal("3:2", CropGeometry.DeriveRatio(start, 2));
        var crop = Drag(start, handle, dx, dy, CropGeometry.TargetRatio(start, "3:2", 2)).Crop!;

        Assert.Equal(1.5, CropGeometry.DraftPixelRatio(crop, 2), 12);
        Assert.InRange(crop.Left, 0, 1);
        Assert.InRange(crop.Right, 0, 1);
        Assert.InRange(crop.Top, 0, 1);
        Assert.InRange(crop.Bottom, 0, 1);
        Assert.True(crop.Right - crop.Left >= CropGeometry.MinCropSize - 1e-12);
        Assert.True(crop.Bottom - crop.Top >= CropGeometry.MinCropSize - 1e-12);
    }

    [AvaloniaTheory]
    [InlineData("TopLeft", -1, -1)]
    [InlineData("TopRight", 1, -1)]
    [InlineData("BottomLeft", -1, 1)]
    [InlineData("BottomRight", 1, 1)]
    [InlineData("TopCenter", 0, -1)]
    [InlineData("BottomCenter", 0, 1)]
    [InlineData("MiddleLeft", -1, 0)]
    [InlineData("MiddleRight", 1, 0)]
    public void ApproximateMinimumAtEveryImageCornerPreservesRatio(string handle, double dx, double dy)
    {
        foreach (var ratio in new[] { .75, 4d / 3 })
        foreach (var drift in new[] { .996, 1.004 })
        foreach (var (right, bottom) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            var width = Math.Max(CropGeometry.MinCropSize, CropGeometry.MinCropSize * ratio * drift);
            var height = width / (ratio * drift);
            var start = new CropRegion
            {
                Left = right ? 1 - width : 0, Right = right ? 1 : width,
                Top = bottom ? 1 - height : 0, Bottom = bottom ? 1 : height
            };
            var crop = Drag(start, handle, dx, dy, ratio).Crop!;

            Assert.Equal(ratio, (crop.Right - crop.Left) / (crop.Bottom - crop.Top), 12);
            Assert.InRange(crop.Left, 0, 1);
            Assert.InRange(crop.Right, 0, 1);
            Assert.InRange(crop.Top, 0, 1);
            Assert.InRange(crop.Bottom, 0, 1);
            Assert.True(crop.Right - crop.Left >= CropGeometry.MinCropSize - 1e-12);
            Assert.True(crop.Bottom - crop.Top >= CropGeometry.MinCropSize - 1e-12);
        }
    }

    [AvaloniaTheory]
    [InlineData(6)]
    [InlineData(1d / 6)]
    public void PanoramaDragAfterRefusedSwapKeepsOriginalRatioAndBounds(double frameRatio)
    {
        var draft = new CropRegion();
        var unchanged = CropGeometry.Swap(draft, frameRatio);
        var crop = Drag(unchanged, "TopCenter", 0, -1,
            CropGeometry.TargetRatio(unchanged, "Original", frameRatio)).Crop!;

        Assert.Equal(frameRatio, CropGeometry.DraftPixelRatio(crop, frameRatio), 12);
        Assert.InRange(crop.Left, 0, 1);
        Assert.InRange(crop.Top, 0, 1);
        Assert.InRange(crop.Right, 0, 1);
        Assert.InRange(crop.Bottom, 0, 1);
        Assert.True(crop.Right - crop.Left >= CropGeometry.MinCropSize);
        Assert.True(crop.Bottom - crop.Top >= CropGeometry.MinCropSize);
    }

    [AvaloniaTheory]
    [InlineData("TopLeft", .1, .1, .4, .3, .8, .5)]
    [InlineData("TopRight", .1, .1, .2, .3, .6, .5)]
    [InlineData("BottomLeft", .1, .1, 0, .2, .8, .6)]
    [InlineData("BottomRight", .1, .1, .2, .2, 1, .6)]
    [InlineData("TopCenter", 0, .1, .3, .3, .7, .5)]
    [InlineData("BottomCenter", 0, .1, .1, .2, .9, .6)]
    [InlineData("MiddleLeft", .1, 0, .3, .225, .8, .475)]
    [InlineData("MiddleRight", .1, 0, .2, .175, .9, .525)]
    public void CustomLockedDragPreservesExistingAnchors(string handle, double dx, double dy,
        double left, double top, double right, double bottom)
    {
        var start = new CropRegion { Left = .2, Top = .2, Right = .8, Bottom = .5 };
        var overlay = Drag(start, handle, dx, dy, CropGeometry.TargetRatio(start, "Custom", 4d / 3));

        Assert.Equal(left, overlay.Crop!.Left, 12);
        Assert.Equal(top, overlay.Crop.Top, 12);
        Assert.Equal(right, overlay.Crop.Right, 12);
        Assert.Equal(bottom, overlay.Crop.Bottom, 12);
    }

    [AvaloniaTheory]
    [InlineData(-10)]
    [InlineData(10)]
    public void LockedDragHonorsTargetMinimumAndBounds(double delta)
    {
        var overlay = Drag(new CropRegion { Left = .2, Top = .2, Right = .8, Bottom = .8 },
            "BottomRight", delta, delta, 1.125);
        var crop = overlay.Crop!;

        Assert.Equal(1.125, (crop.Right - crop.Left) / (crop.Bottom - crop.Top), 12);
        Assert.True(crop.Right - crop.Left >= .05 - 1e-12);
        Assert.True(crop.Bottom - crop.Top >= .05 - 1e-12);
        Assert.InRange(crop.Right, 0, 1);
        Assert.InRange(crop.Bottom, 0, 1);
    }

    [AvaloniaFact]
    public async Task UnlockedPointerDragUpdatesDerivedLabelThroughViewer()
    {
        await DevelopToolsBaselineTests.WithScene("crop", 1200, 700, async (vm, scope) =>
        {
            // test-teardown-policy: allow - WithScene owns this MainWindow scope.
            scope.Show();
            Dispatcher.UIThread.RunJobs();
            var window = scope.Window!;
            window.UpdateLayout();
            vm.ChooseCropRatio("1:1");
            vm.IsCropAspectLocked = false;
            var overlay = window.GetVisualDescendants().OfType<CropOverlayControl>().Single();
            var crop = vm.CurrentCrop!;
            var start = overlay.TranslatePoint(new Point(crop.Right * overlay.Bounds.Width,
                (crop.Top + crop.Bottom) / 2 * overlay.Bounds.Height), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(start - new Vector(23, 0));
            window.MouseUp(start - new Vector(23, 0), MouseButton.Left);
            Assert.Equal("Custom", vm.CropRatio);
            var picker = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "CropRatioPicker");
            Assert.Equal("Custom", picker.SelectedItem);
            await Task.CompletedTask;
        });
    }

    private static CropOverlayControl Drag(CropRegion start, string handle, double dx, double dy, double ratio)
    {
        var overlay = new CropOverlayControl { Crop = start.Clone() };
        var type = typeof(CropOverlayControl);
        type.GetField("_dragStartCrop", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(overlay, start);
        var handleType = type.GetNestedType("DragHandle", BindingFlags.NonPublic)!;
        type.GetMethod("ApplyLockedAspectDrag", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(overlay, [Enum.Parse(handleType, handle), dx, dy, ratio]);

        return overlay;
    }
}

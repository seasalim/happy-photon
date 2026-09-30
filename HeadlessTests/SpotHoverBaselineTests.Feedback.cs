using System.Reflection;
using Avalonia;
using Avalonia.Media;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SpotHoverBaselineTests
{
    private static void AssertHover(string name, SpotsOverlayControl overlay, MainWindowViewModel vm, Point point)
    {
        var outcome = FrozenPressOutcome(name);
        var action = Enum.Parse<SpotHandle>(outcome.Split("gesture=")[1].Split(';')[0]);
        var id = outcome.Split("selected=")[1].Split(';')[0];
        var expected = id == "new" ? null : vm.Spots.Single(s => s.Id.StartsWith(id.ToLowerInvariant()));
        var target = ((SpotHandle Handle, Repair? Spot))typeof(SpotsOverlayControl)
            .GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
        Assert.Equal(action, target.Handle);
        Assert.Same(expected, target.Spot);
        var cursor = action switch
        {
            SpotHandle.Create => "None",
            SpotHandle.Edge => name switch
            {
                "edge-45" or "edge-225" => "TopLeftCorner",
                "edge-90" or "edge-270" => "SizeNorthSouth",
                "edge-135" or "edge-315" => "TopRightCorner",
                _ => "SizeWestEast"
            },
            _ => "SizeAll"
        };
        Assert.Equal(cursor, overlay.Cursor?.ToString());
        AssertHighlight(overlay, vm, expected, action);
        var circles = Circles(overlay);
        Assert.Equal(action == SpotHandle.Create ? 18 : 16, circles.Length);

        if (action == SpotHandle.Create)
        {
            Assert.Contains(circles, c => ((Vector)(c.Geometry!.Bounds.Center - point)).Length < .01);
        }
    }

    private static GeometryDrawing[] Circles(SpotsOverlayControl overlay)
    {
        var group = new DrawingGroup();
        using (var context = group.Open()) overlay.Render(context);

        return Flatten(group).OfType<GeometryDrawing>()
            .Where(d => d.Geometry != null && d.Pen != null).ToArray();
    }

    private static GeometryDrawing[] Highlights(SpotsOverlayControl overlay) =>
        Circles(overlay).Where(d => d.Pen!.Thickness == 2).ToArray();

    private static void AssertHighlight(SpotsOverlayControl overlay, MainWindowViewModel vm, Repair? spot, SpotHandle action)
    {
        if (spot == null)
        {
            Assert.Empty(Highlights(overlay));
            return;
        }

        var highlight = Assert.Single(Highlights(overlay));
        var map = vm.SpotDisplayMap!;
        var (u, v) = action == SpotHandle.Source
            ? RepairGeometry.ClampSource(spot, map.BaseWidth, map.BaseHeight) : (spot.U, spot.V);
        var center = overlay.ToCanvas(new(u, v));
        Assert.InRange(((Vector)(highlight.Geometry!.Bounds.Center - center)).Length, 0, .01);
        Assert.Equal(((ISolidColorBrush)HappyPhotonColors.CropBorder).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(highlight.Pen!.Brush).Color);
    }
}

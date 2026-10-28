using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

internal static partial class SpotHoverBaselineTestSupport
{
    // Frozen from real press-and-drag observations on 62ab987, without calling HitHandle.
    internal static string FrozenPressOutcome(string point) => point switch
    {
        "empty" => "selected=new; gesture=Create; changed=new spot",
        "selected-destination" => "selected=A; gesture=Destination; changed=A.destination",
        "selected-source" => "selected=A; gesture=Source; changed=A.source",
        "B-body" or "B-edge" => "selected=B; gesture=Destination; changed=B.destination",
        "C-body" or "C-edge" or "B-C-overlap" => "selected=C; gesture=Destination; changed=C.destination",
        "D-edge" => "selected=D; gesture=Destination; changed=D.destination",
        _ => "selected=A; gesture=Edge; changed=A.radius"
    };

    internal static List<Repair> FixtureSpots() =>
    [
        new() { Id = "a0000000000000000000000000000000", U = .3125, V = .375, Su = .625, Sv = .375, Radius = .03125 },
        new() { Id = "b0000000000000000000000000000000", U = .6875, V = .6875, Su = .5, Sv = .875, Radius = .03125 },
        new() { Id = "c0000000000000000000000000000000", U = .71875, V = .6875, Su = .5, Sv = .75, Radius = .03125 },
        new() { Id = "d0000000000000000000000000000000", U = .3515625, V = .375, Su = .5, Sv = .25, Radius = .015625 }
    ];

    internal static List<(string Name, Point Point)> Grid(int width, int height)
    {
        var grid = new List<(string, Point)>
        {
            ("empty", new(.125, .125)),
            ("selected-destination", new(.3125, .375)),
            ("selected-source", new(.625, .375)),
            ("B-body", new(.671875, .6875)),
            ("B-edge", new(.65625, .6875)),
            ("C-body", new(.734375, .6875)),
            ("C-edge", new(.75, .6875)),
            ("D-body-selected-edge-overlap", new(.3515625, .375)),
            ("D-edge", new(.3671875, .375)),
            ("B-C-overlap", new(.703125, .6875))
        };

        for (var degrees = 0; degrees < 360; degrees += 45)
        {
            var angle = degrees * Math.PI / 180;
            grid.Add(($"edge-{degrees}", new(.3125 + .03125 * Math.Cos(angle),
                .375 + .03125 * width / height * Math.Sin(angle))));
        }

        return grid;
    }

    internal static string Changes(IReadOnlyList<Repair> before, IReadOnlyList<Repair> after)
    {
        var changes = new List<string>();

        foreach (var spot in after)
        {
            var old = before.FirstOrDefault(s => s.Id == spot.Id);

            if (old == null)
            {
                changes.Add("new spot");
                continue;
            }

            if ((old.U, old.V) != (spot.U, spot.V)) changes.Add($"{spot.Id[..1].ToUpperInvariant()}.destination");
            if ((old.Su, old.Sv) != (spot.Su, spot.Sv)) changes.Add($"{spot.Id[..1].ToUpperInvariant()}.source");
            if (old.Radius != spot.Radius) changes.Add($"{spot.Id[..1].ToUpperInvariant()}.radius");
        }

        return string.Join(",", changes);
    }

    internal static IEnumerable<Drawing> Flatten(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
            {
                foreach (var item in Flatten(child)) yield return item;
            }
        }
        else
        {
            yield return drawing;
        }
    }

    internal static string Drawing(SpotsOverlayControl overlay)
    {
        var group = new DrawingGroup();
        using (var context = group.Open()) overlay.Render(context);
        var circles = Flatten(group).OfType<GeometryDrawing>()
            .Where(d => d.Pen != null && d.Geometry != null).ToArray();
        var values = circles.Select(d => $"{d.Geometry!.GetType().Name}:{d.Geometry.Bounds.Width:F2}x{d.Geometry.Bounds.Height:F2}@{d.Geometry.Bounds.Center.X:F2},{d.Geometry.Bounds.Center.Y:F2}/{d.Pen!.Thickness}/{(d.Pen.DashStyle == null ? "solid" : "dash")}");

        return $"geometry-strokes={circles.Length} [{string.Join(";", values)}]";
    }
}

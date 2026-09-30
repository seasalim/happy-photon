using System.Reflection;
using Avalonia;
using HappyPhoton.Models;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsHoverBaselineTests
{
    private static string ExpectedCursor(string selected, string zone) => zone switch
    {
        "center" => "SizeAll",
        "radial-pin" => selected == "radial" ? "SizeAll" : "Hand",
        "linear-pin" => selected == "linear" ? "SizeAll" : "Hand",
        "brush-pin" => "Hand",
        "x+" or "x-" or "handle-pin-overlap" or "ring-45" or "ring-225" => "TopLeftCorner",
        "y+" or "y-" or "feather+" or "feather-" => "TopRightCorner",
        "ring-135" or "ring-315" => "SizeWestEast",
        "rotation" or "direction" => "Cross",
        _ => "Arrow"
    };

    private static (string Gesture, string? Pin) HoverAction(LocalsOverlayControl overlay)
    {
        var target = typeof(LocalsOverlayControl).GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(overlay)!;
        object? Property(string name) => target.GetType().GetProperty(name)!.GetValue(target);
        var gesture = (bool)Property("Hue")! ? "Hue" : (bool)Property("Brush")! && Property("Pin") == null
            ? "BrushStroke" : Property("Handle")?.ToString() ?? "none";

        return (gesture, (Property("Pin") as LocalAdjustment)?.Id);
    }

    private static void AssertPressParity((string Gesture, string? Pin) hover, MainWindowViewModel vm)
    {
        Assert.Equal(hover.Gesture, Gesture(vm));
        if (hover.Pin != null) Assert.Equal(hover.Pin, vm.SelectedLocal?.Id);
    }
}

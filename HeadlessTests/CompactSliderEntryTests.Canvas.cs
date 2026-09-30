using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class CompactSliderEntryTests
{
    [AvaloniaFact]
    public async Task ClickAwayCommitsSpotSizeBeforeDraggingSelectedSpot()
    {
        await using var s = await Session.Create(new EditSettings
        {
            Repairs = [new() { U = .3, V = .4, Su = .7, Sv = .4, Radius = .02 }]
        }, largeFrame: true);
        await s.Vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        s.Vm.SelectedSpot = s.Vm.Spots.Single();
        var before = s.Vm.SelectedSpot with { };
        var overlay = s.Window.GetVisualDescendants().OfType<SpotsOverlayControl>().Single();
        s.Open(s.Slider("Size", typeof(SpotsEditSection)));
        s.Window.KeyTextInput("3.5");
        var point = overlay.TranslatePoint(overlay.ToCanvas(new(before.U, before.V)), s.Window)!.Value;
        s.Window.MouseDown(point, MouseButton.Left);
        Assert.True(s.Vm.IsSpotsGestureActive);
        s.Window.MouseMove(point + new Vector(30, 20), RawInputModifiers.LeftMouseButton);
        s.Window.MouseUp(point + new Vector(30, 20), MouseButton.Left);
        await s.Drain();

        Assert.Equal(3.5, s.Vm.SpotSize, 8);
        Assert.Equal(.035, s.Vm.SelectedSpot!.Radius, 8);
        Assert.NotEqual(before.U, s.Vm.SelectedSpot.U);
        Assert.False(s.Vm.IsSpotsGestureActive);
    }

    [AvaloniaFact]
    public async Task ClickAwayCommitsLocalExposureBeforeCancelledDrag()
    {
        await using var s = await Session.Create(new EditSettings
        {
            Locals = [new() { Cu = .4, Cv = .4 }]
        }, largeFrame: true);
        await s.Vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        s.Vm.SelectedLocal = s.Vm.Locals.Single();
        var before = s.Vm.SelectedLocal with { };
        var overlay = s.Window.GetVisualDescendants().OfType<LocalsOverlayControl>().Single();
        s.Open(s.Slider("Exposure", typeof(LocalsEditSection)));
        s.Window.KeyTextInput("1.5");
        var canvas = LocalsOverlayControl.ToCanvas(before, s.Vm.LocalsFrame!.Value, overlay.Bounds.Size);
        var point = overlay.TranslatePoint(canvas, s.Window)!.Value;
        s.Window.MouseDown(point, MouseButton.Left);
        Assert.True(s.Vm.IsLocalsGestureActive);
        s.Window.MouseMove(point + new Vector(30, 20), RawInputModifiers.LeftMouseButton);
        Assert.NotEqual(before.Cu, s.Vm.SelectedLocal!.Cu);
        s.Key(Key.Escape);
        s.Window.MouseUp(point + new Vector(30, 20), MouseButton.Left);
        await s.Drain();

        Assert.Equal(1.5, s.Vm.SelectedLocal!.Exposure);
        Assert.Equal(before.Cu, s.Vm.SelectedLocal.Cu);
        Assert.Equal(before.Cv, s.Vm.SelectedLocal.Cv);
        Assert.False(s.Vm.IsLocalsGestureActive);
    }

    [AvaloniaFact]
    public async Task ClickAwayCommitsBrushFeatherBeforePainting()
    {
        await using var s = await Session.Create(largeFrame: true);
        await s.Vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        s.Vm.AddBrushCommand.Execute(null);
        var overlay = s.Window.GetVisualDescendants().OfType<LocalsOverlayControl>().Single();
        s.Open(s.Slider("Feather", typeof(LocalsEditSection)));
        s.Window.KeyTextInput("35");
        var point = s.Center(overlay);
        s.Window.MouseDown(point, MouseButton.Left);
        Assert.True(s.Vm.IsBrushStrokeActive);
        s.Window.MouseMove(point + new Vector(30, 20), RawInputModifiers.LeftMouseButton);
        s.Window.MouseUp(point + new Vector(30, 20), MouseButton.Left);
        await s.Drain();

        Assert.Equal(35, s.Vm.BrushFeather);
        Assert.Equal(.35, Assert.Single(s.Vm.SelectedLocal!.Strokes!).Feather, 8);
        Assert.False(s.Vm.IsBrushStrokeActive);
    }
}

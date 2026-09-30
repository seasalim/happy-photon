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

public sealed partial class CompactSliderEntryTests
{
    [AvaloniaFact]
    public async Task BrushFeatherSavesOnlyPreference()
    {
        await using var s = await Session.Create();
        await s.Vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        s.Vm.AddBrushCommand.Execute(null);
        var saved = new TaskCompletionSource<AppSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Vm.PersistAppSettingsAsync = () =>
        {
            var settings = new AppSettings();
            s.Vm.CaptureBrushPreferences(settings);
            saved.TrySetResult(settings);

            return Task.CompletedTask;
        };
        s.Open(s.Slider("Feather", typeof(LocalsEditSection)));
        s.Window.KeyTextInput("35%");
        s.Key(Key.Enter);
        await s.Drain();
        var preferences = await saved.Task.WaitAsync(TestWaits.Condition);
        Assert.Equal(35, preferences.BrushFeather);
        Assert.Equal(35, s.Vm.BrushFeather);
        Assert.Equal(0, s.Steps);
        Assert.Empty(s.Vm.Locals);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpotsSizeKeepsSelectedRepairOrPreferenceOwnership(bool selected)
    {
        await using var s = await Session.Create(selected ? new EditSettings
        {
            Repairs = [new() { U = .3, V = .4, Su = .7, Sv = .4, Radius = .02 }]
        } : null);
        await s.Vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        s.Vm.SelectedSpot = s.Vm.Spots.FirstOrDefault();
        var count = s.Steps;
        s.Open(s.Slider("Size", typeof(SpotsEditSection)));
        s.Window.KeyTextInput("3.5");
        s.Key(Key.Enter);
        await s.Drain();
        Assert.Equal(3.5, s.Vm.SpotSize, 8);
        Assert.Equal(count + (selected ? 1 : 0), s.Steps);

        if (selected)
        {
            Assert.Equal(.035, s.Vm.SelectedSpot!.Radius, 8);
            await s.Vm.UndoCommand.ExecuteAsync(null);
            Assert.Equal(.02, s.Vm.Spots.Single().Radius, 8);
        }
        else
        {
            var settings = new AppSettings();
            s.Vm.CaptureBrushPreferences(settings);
            Assert.Equal(3.5, settings.SpotSize);
            Assert.Empty(s.Vm.Spots);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingEqualValuedMaskPinsCommitsOnlyOutgoingDraft(bool reverse)
    {
        await using var s = await Session.Create(new EditSettings
        {
            Locals = [new() { Cu = .25, Cv = .4 }, new() { Cu = .75, Cv = .6, Ordinal = 2 }]
        }, largeFrame: true);
        await s.Vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var locals = s.Vm.Locals.ToArray();
        var outgoing = locals[reverse ? 1 : 0];
        var incoming = locals[reverse ? 0 : 1];
        s.Vm.SelectedLocal = outgoing;
        var count = s.Steps;
        var overlay = s.Window.GetVisualDescendants().OfType<LocalsOverlayControl>().Single();
        var slider = s.Slider("Exposure", typeof(LocalsEditSection));
        s.Open(slider);
        s.Window.KeyTextInput("1.5");
        var canvas = LocalsOverlayControl.ToCanvas(incoming, s.Vm.LocalsFrame!.Value, overlay.Bounds.Size);
        s.Click(overlay.TranslatePoint(canvas, s.Window)!.Value);
        await s.Drain();

        Assert.Equal(incoming.Id, s.Vm.SelectedLocal!.Id);
        Assert.False(slider.IsEditingValue);
        Assert.Equal(0, s.Vm.SelectedLocal.Exposure);
        Assert.Equal(1.5, s.Vm.Locals.Single(local => local.Id == outgoing.Id).Exposure);
        Assert.Equal(count + 1, s.Steps);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingEqualValuedSpotsCommitsOnlyOutgoingDraft(bool reverse)
    {
        await using var s = await Session.Create(new EditSettings
        {
            Repairs = [new() { U = .25, V = .25, Su = .5, Sv = .25, Radius = .02 },
                new() { U = .75, V = .75, Su = .5, Sv = .75, Radius = .02 }]
        }, largeFrame: true);
        await s.Vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        var spots = s.Vm.Spots.ToArray();
        var outgoing = spots[reverse ? 1 : 0];
        var incoming = spots[reverse ? 0 : 1];
        s.Vm.SelectedSpot = outgoing;
        var count = s.Steps;
        var overlay = s.Window.GetVisualDescendants().OfType<SpotsOverlayControl>().Single();
        var slider = s.Slider("Size", typeof(SpotsEditSection));
        s.Open(slider);
        s.Window.KeyTextInput("3.5");
        s.Click(overlay.TranslatePoint(overlay.ToCanvas(new(incoming.U, incoming.V)), s.Window)!.Value);
        await s.Drain();

        Assert.Equal(incoming.Id, s.Vm.SelectedSpot!.Id);
        Assert.False(slider.IsEditingValue);
        Assert.Equal(.02, s.Vm.SelectedSpot.Radius);
        Assert.Equal(.035, s.Vm.Spots.Single(spot => spot.Id == outgoing.Id).Radius, 8);
        Assert.Equal(count + 1, s.Steps);
    }

    [AvaloniaFact]
    public async Task ExportAndWatermarkEntriesKeepSettingsOwnership()
    {
        await using var s = await Session.Create();
        s.Vm.WorkspaceMode = WorkspaceMode.Export;
        var count = s.Steps;
        s.Open(s.Slider("Quality"));
        s.Window.KeyTextInput("85%");
        s.Key(Key.Enter);
        Assert.Equal(85, s.Vm.ExportSettings.Quality);
        s.Vm.IsWatermarkExpanded = true;
        s.Vm.ExportSettings.Watermark.Enabled = true;
        s.Open(s.Slider("Opacity"));
        s.Window.KeyTextInput("35%");
        s.Key(Key.Enter);
        await s.Drain();
        Assert.Equal(35, s.Vm.ExportSettings.Watermark.Opacity);
        Assert.Equal(count, s.Steps);
    }

    [AvaloniaFact]
    public async Task LocalAngleWrapsAndKeepsGeometryHistoryLabel()
    {
        await using var s = await Session.Create(new EditSettings { Locals = [new()] });
        await s.Vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        s.Vm.SelectedLocal = s.Vm.Locals[0];
        s.Vm.IsLocalGeometryExpanded = true;
        var count = s.Steps;
        s.Open(s.Slider("Angle", typeof(LocalsEditSection)));
        s.Window.KeyTextInput("370°");
        s.Key(Key.Enter);
        await s.Drain();
        Assert.Equal(10, s.Vm.SelectedLocal!.Angle);
        Assert.Equal(count + 1, s.Steps);
        Assert.Equal("Local geometry", s.Vm.HistoryEntries[0].Label);
    }
}

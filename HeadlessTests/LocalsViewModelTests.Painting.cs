using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class LocalsViewModelTests
{
    [AvaloniaFact]
    public async Task BrushCreationStrokePreferencesClearAndHistory()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var settingsService = new AppSettingsService(catalog);
        vm.PersistAppSettingsAsync = () =>
        {
            var settings = new AppSettings(); vm.CaptureBrushPreferences(settings);
            return settingsService.SavePreferencesAsync(settings);
        };
        vm.AddBrushCommand.Execute(null);
        Assert.True(vm.IsBrushSectionVisible); Assert.True(vm.IsLocalMaskVisible);
        Assert.Equal("Paint to create · Escape cancels", vm.LocalsInstruction);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null); Assert.Empty(vm.Locals);
        var history = vm.HistoryEntries.Count;
        vm.BrushSize = 70; vm.BrushFeather = 27; vm.BrushFlow = 35;
        await vm.PersistAppSettingsAsync();
        var saved = await settingsService.LoadAsync();
        Assert.Equal(70, saved.BrushSize); Assert.Equal(27, saved.BrushFeather); Assert.Equal(35, saved.BrushFlow);
        vm.RestoreBrushPreferences(saved);
        Assert.Equal(history, vm.HistoryEntries.Count);
        Assert.True(vm.BeginBrushStroke(new(.2, .2)));
        Assert.False(vm.ExtendBrushStroke(new(.20001, .20001), 1000));
        Assert.True(vm.ExtendBrushStroke(new(.4, .4), 1000));
        Assert.True(vm.ExtendBrushStroke(new(.4001, .4001), 1000, final: true));
        var stroke = vm.LiveBrushStroke!;
        Assert.Equal(3, stroke.Points.Count); Assert.Equal(.27, stroke.Feather); Assert.Equal(.35, stroke.Flow);
        Assert.Equal(vm.BrushRadius, stroke.Radius);
        Assert.False(vm.ShowLocalMask); Assert.True(vm.IsLocalMaskVisible);
        Assert.Equal(history, vm.HistoryEntries.Count);
        await vm.CompleteLocalsGestureAsync();
        Assert.Equal("Add Brush", vm.HistoryEntries[0].Label);
        Assert.Equal("Brush 1", vm.SelectedLocal!.Name); Assert.Equal("✎", vm.SelectedLocalRow!.Glyph);
        Assert.True(vm.IsLocalMaskVisible);
        Assert.Equal(EditSettingsJson.Serialize(vm.SelectedImage!.EditSettings),
            EditSettingsJson.Serialize(EditSettingsJson.Deserialize(EditSettingsJson.Serialize(vm.SelectedImage.EditSettings), out _)));
        vm.BrushMode = "erase"; vm.BrushFlow = 100;
        await vm.PersistAppSettingsAsync(); Assert.Equal("erase", (await settingsService.LoadAsync()).BrushMode);
        Assert.True(vm.BeginBrushStroke(new(.8, .6), straight: true));
        Assert.Equal(stroke.Points[^1], vm.LiveBrushStroke!.Points[0]);
        Assert.Equal(2, vm.LiveBrushStroke.Points.Count);
        await vm.CompleteLocalsGestureAsync(); Assert.Equal("Erase stroke", vm.HistoryEntries[0].Label);
        vm.BrushMode = "paint";
        Assert.True(vm.BeginBrushStroke(new(.1, .1)));
        await vm.CompleteLocalsGestureAsync(); Assert.Equal("Brush stroke", vm.HistoryEntries[0].Label);
        Assert.Single(vm.SelectedLocal!.Strokes![^1].Points);
        await vm.ClearBrushStrokesCommand.ExecuteAsync(null);
        Assert.Empty(Assert.Single(vm.Locals).Strokes!); Assert.Equal("Clear strokes", vm.HistoryEntries[0].Label);
        await vm.UndoCommand.ExecuteAsync(null); Assert.Equal(3, vm.SelectedLocal!.Strokes!.Count);
        Assert.Equal(100, vm.BrushFlow); Assert.Equal("paint", vm.BrushMode);
        vm.LocalExposure = 1; Assert.False(vm.IsLocalMaskVisible);
        Assert.True(vm.BeginBrushStroke(new(.3, .3))); Assert.True(vm.IsLocalMaskVisible);
        vm.EscapeLocals(); Assert.False(vm.IsLocalMaskVisible); Assert.False(vm.ShowLocalMask);
    }

    [AvaloniaTheory]
    [InlineData("escape")] [InlineData("undo")] [InlineData("navigation")] [InlineData("selection")]
    public async Task BrushDiscardRestoresDocumentExactly(string action)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        vm.AddBrushCommand.Execute(null); vm.BeginBrushStroke(new(.2, .2)); await vm.CompleteLocalsGestureAsync();
        var image = vm.SelectedImage!; var before = EditSettingsJson.Serialize(image.EditSettings); var history = vm.HistoryEntries.Count;
        Assert.True(vm.BeginBrushStroke(new(.4, .4))); Assert.True(vm.ExtendBrushStroke(new(.8, .7), 1000));
        if (action == "escape") vm.EscapeLocals();
        else if (action == "undo") await vm.UndoCommand.ExecuteAsync(null);
        else if (action == "selection") vm.SelectedLocal = vm.Locals[0];
        else vm.SelectedImage = new ImageFile(_fixture.Path("next.jpg"));
        Assert.False(vm.IsLocalsGestureActive); Assert.Equal(before, EditSettingsJson.Serialize(image.EditSettings));
        if (action != "navigation") Assert.Equal(history, vm.HistoryEntries.Count);
    }

    [AvaloniaFact]
    public async Task BrushCapsAreDocumentWideAndFinalPointCannotOverflow()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog, new TestTimeProvider());
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        vm.SelectedImage!.EditSettings.Locals = [new() { Type = "brush", Strokes =
            [.. Enumerable.Range(0, 95).Select(i => new LocalBrushStroke { Points =
                [.. Enumerable.Range(0, i < 5 ? 42 : 41).Select(p => new LocalBrushPoint(p * 100, i * 100))] })] }];
        vm.SelectedLocal = vm.Locals[0];
        Assert.True(vm.BeginBrushStroke(new(.1, .1)));
        for (var i = 1; i < 100; i++) Assert.True(vm.ExtendBrushStroke(new(i % 2 == 0 ? .1 : .9, .5), 1000));
        Assert.False(vm.ExtendBrushStroke(new(2, 2), 1000, final: true));
        Assert.Contains("4,000", vm.LocalsInstruction);
        await vm.CompleteLocalsGestureAsync();
        Assert.Equal(4000, vm.Locals.Sum(l => l.Strokes!.Sum(s => s.Points.Count)));
        Assert.False(vm.BeginBrushStroke(new(.5, .5)));
        var loaded = EditSettingsJson.Deserialize(EditSettingsJson.Serialize(vm.SelectedImage.EditSettings), out _);
        Assert.Equal(96, loaded.Locals![0].Strokes!.Count);
        vm.AddBrushCommand.Execute(null); Assert.False(vm.BeginBrushStroke(new(.5, .5)));
        vm.EscapeLocals();
        vm.SelectedLocal!.Strokes = [.. Enumerable.Range(0, 96).Select(_ => new LocalBrushStroke { Points = [new(0, 0)] })];
        Assert.Contains("96", vm.LocalsInstruction); Assert.False(vm.BeginBrushStroke(new(.5, .5)));
        await vm.ClearBrushStrokesCommand.ExecuteAsync(null);
        Assert.True(vm.BeginBrushStroke(new(-2, 3)));
        Assert.Equal(new LocalBrushPoint(-16384, 32768), vm.LiveBrushStroke!.Points[0]);
        vm.EscapeLocals();
    }

    [AvaloniaFact]
    public async Task BrushThrottleDispatchesDuringContinuousInputAndTrailsWithoutHistory()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog); await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        var renders = 0;
        vm.ImageService.Previews.RenderGateAsync = () => { renders++; return Task.CompletedTask; };
        var history = vm.HistoryEntries.Count;
        vm.AddBrushCommand.Execute(null); Assert.True(vm.BeginBrushStroke(new(.1, .1)));
        await TestWaits.UntilAsync(() => renders == 1);
        for (var tick = 0; tick < 3; tick++)
        {
            for (var i = 0; i < 5; i++)
            {
                Assert.True(vm.ExtendBrushStroke(new(i % 2 == 0 ? .8 : .2, .2 + tick * .1), 1000));
                clock.Advance(TimeSpan.FromMilliseconds(10));
                Assert.Equal(tick + 1, renders);
            }
            clock.Advance(TimeSpan.FromMilliseconds(10));
            await TestWaits.UntilAsync(() => renders == tick + 2);
            Assert.True(vm.IsBrushStrokeActive); Assert.Equal(history, vm.HistoryEntries.Count);
        }
        Assert.True(vm.ExtendBrushStroke(new(.4, .9), 1000));
        clock.Advance(TimeSpan.FromMilliseconds(60));
        await TestWaits.UntilAsync(() => renders == 5);
        vm.EscapeLocals();
        Assert.Equal(history, vm.HistoryEntries.Count);
    }
}

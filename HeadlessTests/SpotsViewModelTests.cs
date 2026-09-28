using Avalonia;
using Avalonia.Headless.XUnit;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SpotsViewModelTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("spots-vm");

    private MainWindowViewModel CreateVm(CatalogService catalog, TimeProvider? clock = null) =>
        _fixture.CreateViewModel(catalog, new LocalTestLoader(width: 1200, height: 800),
            _ => Task.CompletedTask, new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), timeProvider: clock);

    private async Task Prepare(MainWindowViewModel vm, CatalogService catalog)
    {
        var image = new ImageFile(_fixture.Path("photo.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Browse.SetImages([image]);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        Assert.True(vm.CanEditSpots);
    }

    private static async Task CreateSpot(MainWindowViewModel vm, bool manual = false)
    {
        Assert.True(vm.BeginSpotsGesture(SpotHandle.Create, new(.4, .4)));
        if (manual) vm.MoveSpotsGesture(new(.7, .6), 100);
        await vm.CompleteSpotsGestureAsync();
        Assert.NotNull(vm.SelectedSpot);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateMoveSourceResizeDeleteClearAndUndo(bool clone)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        if (clone) await vm.SetSpotModeCommand.ExecuteAsync("clone");
        await CreateSpot(vm, clone);
        Assert.Equal(clone ? "Add Clone spot" : "Add Heal spot", vm.HistoryEntries[0].Label);
        Assert.Single(vm.Spots);
        var original = vm.SelectedSpot! with { };
        if (clone) Assert.InRange(original.Su, .699, .701);
        else Assert.True(Math.Sqrt(Math.Pow((original.Su - original.U) * 1200, 2) +
            Math.Pow((original.Sv - original.V) * 800, 2)) >= original.Radius * 2400);

        foreach (var (handle, point, label) in new[]
        {
            (SpotHandle.Destination, new Point(.5, .45), "Move spot"),
            (SpotHandle.Source, new Point(2, -1), "Move spot source"),
            (SpotHandle.Edge, new Point(.58, .45), "Resize spot")
        })
        {
            var before = vm.SelectedSpot! with { };
            var count = vm.HistoryEntries.Count;
            var press = new Point(before.U + (handle == SpotHandle.Edge ? before.Radius : 0), before.V);
            Assert.True(vm.BeginSpotsGesture(handle, press));
            vm.MoveSpotsGesture(point, 100);
            await vm.CompleteSpotsGestureAsync();
            Assert.Equal(count + 1, vm.HistoryEntries.Count);
            Assert.Equal(label, vm.HistoryEntries[0].Label);
            Assert.Equal(RepairGeometry.ClampSource(vm.SelectedSpot!, 1200, 800), (vm.SelectedSpot!.Su, vm.SelectedSpot.Sv));
            await vm.UndoCommand.ExecuteAsync(null);
            Assert.Equal(before, vm.Spots.Single());
            await vm.RedoCommand.ExecuteAsync(null);
        }
        await vm.DeleteSpotCommand.ExecuteAsync(null);
        Assert.Empty(vm.Spots);
        Assert.Equal("Delete spot", vm.HistoryEntries[0].Label);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Single(vm.Spots);
        await CreateSpot(vm);
        await vm.ClearSpotsCommand.ExecuteAsync(null);
        Assert.Empty(vm.Spots);
        Assert.Equal("Clear spots", vm.HistoryEntries[0].Label);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Spots.Count);
    }

    [AvaloniaFact]
    public async Task EscapeUndoNavigationAndToolSwitchDiscardDrafts()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        Assert.True(vm.BeginSpotsGesture(SpotHandle.Create, new(.2, .3)));
        Assert.True(vm.UndoCommand.CanExecute(null));
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Empty(vm.Spots);
        await CreateSpot(vm);
        var saved = vm.SelectedSpot! with { };
        Assert.True(vm.BeginSpotsGesture(SpotHandle.Destination, new(saved.U, saved.V)));
        vm.MoveSpotsGesture(new(.8, .8), 100);
        Assert.True(vm.EscapeSpots());
        Assert.Equal(saved, vm.SelectedSpot);
        Assert.True(vm.EscapeSpots());
        Assert.Null(vm.SelectedSpot);
        Assert.True(vm.IsSpotsMode);
        Assert.True(vm.EscapeSpots());
        Assert.False(vm.IsSpotsMode);
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        Assert.True(vm.BeginSpotsGesture(SpotHandle.Create, new(.2, .3)));
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
        Assert.False(vm.IsSpotsMode);
        Assert.Single(vm.Spots);
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        Assert.False(vm.IsLocalsMode);
        var image = vm.SelectedImage!;
        Assert.True(vm.BeginSpotsGesture(SpotHandle.Create, new(.2, .3)));
        vm.SelectedImage = null;
        Assert.Single(image.EditSettings.Repairs!);
        Assert.False(vm.IsSpotsGestureActive);
    }

    [AvaloniaFact]
    public async Task CountAndAreaLimitsClampCreationAndResize()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        vm.SelectedImage!.EditSettings.Repairs = Enumerable.Range(0, 64).Select(_ => new Repair { Radius = .002 }).ToList();
        Assert.StartsWith("64 of 64", vm.SpotCount);
        Assert.False(vm.BeginSpotsGesture(SpotHandle.Create, new(.5, .5)));
        vm.SelectedImage.EditSettings.Repairs = Enumerable.Range(0, 6).Select(_ => new Repair { Radius = .1 }).ToList();
        Assert.False(vm.BeginSpotsGesture(SpotHandle.Create, new(.5, .5)));
        Assert.Equal("Spot area limit reached", vm.StatusMessage);
        vm.SelectedImage.EditSettings.Repairs[^1].Radius = .099;
        vm.SpotSize = 10;
        Assert.True(vm.BeginSpotsGesture(SpotHandle.Create, new(.5, .5)));
        var added = vm.SelectedSpot!;
        Assert.InRange(added.Radius, .002, .02);
        await vm.CompleteSpotsGestureAsync();
        Assert.InRange(RepairArea.Sum(vm.Spots), Repair.MaximumArea - 1e-12, Repair.MaximumArea + 1e-12);
        vm.SelectedSpot = vm.Spots[0];
        var selected = vm.SelectedSpot!;
        Assert.True(vm.BeginSpotsGesture(SpotHandle.Edge, new(selected.U + selected.Radius, selected.V)));
        vm.MoveSpotsGesture(new(1, 1), 100);
        await vm.CompleteSpotsGestureAsync();
        Assert.InRange(RepairArea.Sum(vm.Spots), 0, Repair.MaximumArea * (1 + RepairArea.RelativeTolerance));
    }

    public void Dispose() => _fixture.Dispose();
}

using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SpotHoverBaselineTests
{
    [AvaloniaFact]
    public async Task MeasureStationaryChanges()
    {
        using var fixture = new CatalogVmFixture("spot-hover-stationary");
        using var catalog = await fixture.CreateCatalogAsync();
        var path = GoldenTestPaths.Asset("srgb-reference.jpg");
        GoldenTestPaths.RequireReadableFixture(path);
        await using var vm = fixture.CreateViewModel(catalog, new StandardBaseLoader(), _ => Task.CompletedTask);
        var image = new ImageFile(path) { CatalogId = await catalog.GetOrCreateImageAsync(path) };
        image.EditSettings.Repairs = FixtureSpots();
        await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);
        vm.Browse.SetImages([image]);
        vm.IsDevelopMode = true;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.PreviewImage != null && vm.IsHistoryLoaded);
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        vm.SelectedSpot = vm.Spots[0];
        var window = new MainWindow { Width = 1200, Height = 700 };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var overlay = window.GetVisualDescendants().OfType<SpotsOverlayControl>().Single();
        var map = vm.SpotDisplayMap!;
        var edge = Grid(map.BaseWidth, map.BaseHeight).Single(p => p.Name == "edge-90").Point;
        window.MouseMove(overlay.TranslatePoint(overlay.ToCanvas(edge), window)!.Value);
        Assert.True(overlay.Focus());
        Report("before");
        Assert.Equal("SizeNorthSouth", overlay.Cursor?.ToString());
        AssertHighlight(overlay, vm, vm.Spots[0], SpotHandle.Edge);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
        if (vm.HandleEscapeCommand.ExecutionTask is { } escape) await escape;
        Assert.Null(vm.SelectedSpot);
        Report("deselect");
        Assert.Equal("SizeAll", overlay.Cursor?.ToString());
        AssertHighlight(overlay, vm, vm.Spots[0], SpotHandle.Destination);
        vm.SelectedSpot = vm.Spots[0];
        await vm.DeleteSpotCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Spots.Count);
        Report("delete");
        Assert.Equal("None", overlay.Cursor?.ToString());
        Assert.Empty(Highlights(overlay));
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.Spots.Count);
        Report("undo");
        Assert.Equal("SizeAll", overlay.Cursor?.ToString());
        AssertHighlight(overlay, vm, vm.Spots[0], SpotHandle.Destination);
        vm.SelectedSpot = vm.Spots[0];
        var size = vm.SpotSize;
        vm.SpotSize = .2;
        Assert.Equal("None", overlay.Cursor?.ToString());
        Assert.Empty(Highlights(overlay));
        vm.SpotSize = size;
        Assert.Equal("SizeNorthSouth", overlay.Cursor?.ToString());
        AssertHighlight(overlay, vm, vm.Spots[0], SpotHandle.Edge);
        vm.RotateRightCommand.Execute(null);
        await vm.PendingHistoryCommitTask!.WaitAsync(TestWaits.Condition);
        // History completion precedes compositor hit testing and stationary hover feedback.
        await TestWaits.UntilAsync(() =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            return vm.CanEditSpots && overlay.IsPointerOver && overlay.Cursor?.ToString() == "None";
        });
        Assert.NotSame(map, vm.SpotDisplayMap);
        Assert.Equal("None", overlay.Cursor?.ToString());
        Assert.Empty(Highlights(overlay));

        void Report(string phase)
        {
            output.WriteLine($"S4b {phase}: cursor={overlay.Cursor?.ToString() ?? "null"}; selected={vm.SelectedSpot?.Id ?? "null"}; {Drawing(overlay)}");
        }
    }
}


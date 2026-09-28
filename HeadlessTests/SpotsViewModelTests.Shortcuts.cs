using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SpotsViewModelTests
{
    [AvaloniaFact]
    public async Task RealWindowShortcutsAndTextPickerGesturePrecedence()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await CreateSpot(vm);
        var window = new MainWindow { Focusable = true };
        using var scope = TestUiScope.ForMainWindow(window, vm);
        Assert.True(window.Focus());
        var spot = vm.SelectedSpot;
        var history = vm.HistoryEntries.Count;
        Press(Key.A);
        Press(Key.H);
        Assert.True(vm.VisualizeSpots);
        Assert.True(vm.HideSpotCircles);
        Assert.Same(spot, vm.SelectedSpot);
        Assert.Equal(history, vm.HistoryEntries.Count);
        Press(Key.OemCloseBrackets);
        await vm.StepSpotPreferenceCommand.ExecutionTask!;
        Assert.Equal(3.75, vm.SpotSize, 8);
        Assert.Equal(history + 1, vm.HistoryEntries.Count);
        Press(Key.OemOpenBrackets);
        await vm.StepSpotPreferenceCommand.ExecutionTask!;
        Assert.Equal(3, vm.SpotSize, 8);
        Press(Key.OemCloseBrackets, RawInputModifiers.Shift);
        await vm.StepSpotPreferenceCommand.ExecutionTask!;
        Assert.Equal(60, vm.SpotFeather, 8);
        Press(Key.OemOpenBrackets, RawInputModifiers.Shift);
        await vm.StepSpotPreferenceCommand.ExecutionTask!;
        Assert.Equal(50, vm.SpotFeather, 8);
        Press(Key.OemQuestion);
        await vm.NextSpotSourceCommand.ExecutionTask!;
        Assert.Equal("New spot source", vm.HistoryEntries[0].Label);
        Assert.Equal(history + 5, vm.HistoryEntries.Count);

        var content = window.Content;
        var text = new TextBox();
        window.Content = text;
        Dispatcher.UIThread.RunJobs();
        Assert.True(text.Focus());
        AssertSuppressed();
        window.Content = content;
        Assert.True(window.Focus());
        vm.IsWhiteBalancePicking = true;
        AssertSuppressed();
        vm.IsWhiteBalancePicking = false;
        vm.IsLocalHuePicking = true;
        AssertSuppressed();
        vm.IsLocalHuePicking = false;
        Assert.True(vm.BeginSpotsGesture(HappyPhoton.ViewModels.SpotHandle.Destination, new(.4, .4)));
        AssertSuppressed();
        vm.DiscardSpotsGesture();
        var visualize = vm.VisualizeSpots;
        Press(Key.A, RawInputModifiers.Shift);
        Assert.Equal(visualize, vm.VisualizeSpots);

        void AssertSuppressed()
        {
            var json = EditSettingsJson.Serialize(vm.SelectedImage!.EditSettings);
            var count = vm.HistoryEntries.Count;
            var visual = vm.VisualizeSpots;
            var hidden = vm.HideSpotCircles;

            foreach (var key in new[] { Key.A, Key.H, Key.OemQuestion, Key.OemOpenBrackets, Key.OemCloseBrackets })
                Press(key);

            Press(Key.OemOpenBrackets, RawInputModifiers.Shift);
            Press(Key.OemCloseBrackets, RawInputModifiers.Shift);
            Assert.Equal(json, EditSettingsJson.Serialize(vm.SelectedImage.EditSettings));
            Assert.Equal(count, vm.HistoryEntries.Count);
            Assert.Equal(visual, vm.VisualizeSpots);
            Assert.Equal(hidden, vm.HideSpotCircles);
        }

        void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            window.KeyPress(key, modifiers, PhysicalKey.None, null);
            window.KeyRelease(key, modifiers, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task FreshCandidatesAfterLoadMoveResizeUndoAndRedo()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        await CreateSpot(vm, true);
        var image = vm.SelectedImage!;
        vm.SelectedImage = null;
        vm.SelectedImage = image;
        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        vm.SelectedSpot = vm.Spots[0];
        await CheckNext();
        await CheckNext();
        var spot = vm.SelectedSpot!;
        Assert.True(vm.BeginSpotsGesture(HappyPhoton.ViewModels.SpotHandle.Destination, new(spot.U, spot.V)));
        vm.MoveSpotsGesture(new(.5, .45), 100);
        await vm.CompleteSpotsGestureAsync();
        await CheckNext();
        await vm.StepSpotPreferenceCommand.ExecuteAsync("size+");
        await CheckNext();
        await vm.UndoCommand.ExecuteAsync(null);
        await vm.RedoCommand.ExecuteAsync(null);
        vm.SelectedSpot = vm.Spots[0];
        await CheckNext();

        async Task CheckNext()
        {
            await TestWaits.UntilAsync(() =>
            {
                using var ready = vm.ImageService.Previews.AcquireLocalRangeBase(image,
                    image.EditSettings, BaseImage.InteractivePreviewMaxDimension);

                return vm.CanUseSpotShortcuts && ready != null;
            });
            var current = vm.SelectedSpot!;
            using var lease = vm.ImageService.Previews.AcquireLocalRangeBase(vm.SelectedImage!,
                vm.SelectedImage!.EditSettings, BaseImage.InteractivePreviewMaxDimension);
            Assert.NotNull(lease);
            var candidates = AutomaticRepairSource.Rank(lease.Base, current).Select(candidate =>
            {
                var trial = current with
                {
                    Su = Math.Round(candidate.U * Repair.CoordinateScale) / Repair.CoordinateScale,
                    Sv = Math.Round(candidate.V * Repair.CoordinateScale) / Repair.CoordinateScale
                };

                var clamped = RepairGeometry.ClampSource(trial, 1200, 800);

                return (Math.Round(clamped.U * Repair.CoordinateScale) / Repair.CoordinateScale,
                    Math.Round(clamped.V * Repair.CoordinateScale) / Repair.CoordinateScale);
            }).Distinct().ToList();
            var expected = candidates[(candidates.IndexOf((current.Su, current.Sv)) + 1) % candidates.Count];
            var count = vm.HistoryEntries.Count;
            await vm.NextSpotSourceCommand.ExecuteAsync(null);
            Assert.Equal(expected, (vm.SelectedSpot!.Su, vm.SelectedSpot.Sv));
            Assert.Equal(count + 1, vm.HistoryEntries.Count);
            Assert.Equal("New spot source", vm.HistoryEntries[0].Label);
        }
    }

    [AvaloniaFact]
    public async Task ShortcutAreaLimitAndNewSpotPreferences()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        var history = vm.HistoryEntries.Count;
        await vm.StepSpotPreferenceCommand.ExecuteAsync("size+");
        await vm.StepSpotPreferenceCommand.ExecuteAsync("feather-");
        Assert.Equal(3.75, vm.SpotSize);
        Assert.Equal(40, vm.SpotFeather);
        Assert.Equal(history, vm.HistoryEntries.Count);
        vm.SelectedImage!.EditSettings.Repairs = Enumerable.Range(0, 6).Select(_ => new Repair { Radius = .099 }).ToList();
        vm.SelectedSpot = vm.Spots[0];
        await vm.StepSpotPreferenceCommand.ExecuteAsync("size+");
        Assert.InRange(RepairArea.Sum(vm.Spots), 0, Repair.MaximumArea * (1 + RepairArea.RelativeTolerance));
        Assert.Equal("Spot size", vm.HistoryEntries[0].Label);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EdgeClampedSourceAdvancesAfterPersistence(bool undoRedo)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        await using var vm = CreateVm(catalog);
        await Prepare(vm, catalog);
        Assert.True(vm.BeginSpotsGesture(HappyPhoton.ViewModels.SpotHandle.Create, new(.02, .02)));
        await vm.CompleteSpotsGestureAsync();
        var image = vm.SelectedImage!;
        var edgeSource = vm.SelectedSpot! with { };
        var persisted = EditSettingsJson.Deserialize(EditSettingsJson.Serialize(image.EditSettings), out _);
        Assert.NotEqual((edgeSource.Su, edgeSource.Sv), (persisted.Repairs![0].Su, persisted.Repairs[0].Sv));
        Assert.Equal(RepairGeometry.ClampSource(edgeSource, 1200, 800), (edgeSource.Su, edgeSource.Sv));

        if (undoRedo)
        {
            vm.SelectedImage = null;
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
            await vm.UndoCommand.ExecuteAsync(null);
            await vm.RedoCommand.ExecuteAsync(null);
        }
        else
        {
            await catalog.SaveEditSettingsAsync(image.CatalogId, image.EditSettings);
            var saved = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single();
            vm.SelectedImage = null;
            image.EditSettings = saved.EditSettings;
            vm.SelectedImage = image;
            await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);
        }

        vm.SelectedSpot = vm.Spots.Single();
        Assert.Equal(persisted.Repairs[0], vm.SelectedSpot);
        await TestWaits.UntilAsync(() =>
        {
            using var lease = vm.ImageService.Previews.AcquireLocalRangeBase(image,
                image.EditSettings, BaseImage.InteractivePreviewMaxDimension);

            return vm.CanUseSpotShortcuts && lease != null;
        });

        for (var press = 0; press < 4; press++)
        {
            var before = vm.SelectedSpot! with { };
            var count = vm.HistoryEntries.Count;
            await vm.NextSpotSourceCommand.ExecuteAsync(null);
            var after = vm.SelectedSpot!;
            var roundTrip = EditSettingsJson.Deserialize(EditSettingsJson.Serialize(image.EditSettings), out _).Repairs![0];
            Assert.NotEqual(RepairGeometry.ClampSource(before, 1200, 800), RepairGeometry.ClampSource(after, 1200, 800));
            Assert.NotEqual((before.Su, before.Sv), (roundTrip.Su, roundTrip.Sv));
            Assert.Equal((roundTrip.Su, roundTrip.Sv), (after.Su, after.Sv));
            Assert.Equal(count + 1, vm.HistoryEntries.Count);
            Assert.Equal("New spot source", vm.HistoryEntries[0].Label);
        }
    }
}

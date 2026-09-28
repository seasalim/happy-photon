using Avalonia.Controls;
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

public sealed partial class CompactSliderWheelHistoryTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedBurstWaitsForRenderAndFailureRollsBack(bool local)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog, local);
        var image = vm.SelectedImage!;
        var before = image.EditSettings.Clone();
        var historyCount = vm.HistoryEntries.Count;
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Content = panel };
        using var scope = new TestUiScope(window);
        var slider = ExposureSlider(panel, local);
        slider.WheelTimeProvider = new TestTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ImageService.Previews.RenderGateAsync = () =>
        {
            entered.TrySetResult();
            return release.Task;
        };

        try
        {
            CompactSliderWheelTests.Wheel(slider, 0, 3);
            slider.CompleteWheel();
            clock.Advance(TimeSpan.FromMilliseconds(200));
            await entered.Task.WaitAsync(TestWaits.Condition);

            Assert.Equal(historyCount, vm.HistoryEntries.Count);
            var during = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath]
                .Single().EditSettings;
            Assert.True(before.HasSameEdits(during));

            release.TrySetException(new InvalidOperationException("Wheel render failure"));
            if (vm.PendingPreviewDebounceTask is { } preview)
                await preview.WaitAsync(TestWaits.Condition);
            if (vm.PendingHistoryCommitTask is { } commit)
                await commit.WaitAsync(TestWaits.Condition);

            Assert.Equal(0, slider.Value);
            Assert.Equal(0, local ? vm.LocalExposure : vm.Exposure);
            Assert.True(before.HasSameEdits(image.EditSettings));
            var saved = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath]
                .Single().EditSettings;
            Assert.True(before.HasSameEdits(saved));
            Assert.Equal(historyCount, vm.HistoryEntries.Count);
        }
        finally
        {
            release.TrySetResult();
            vm.ImageService.Previews.RenderGateAsync = null;
        }
    }

    [AvaloniaFact]
    public async Task SpotsSelectionCommitsOutgoingBurst()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog, false);
        await vm.ToggleSpotsModeCommand.ExecuteAsync(null);
        Assert.True(vm.BeginSpotsGesture(SpotHandle.Create, new(.4, .4)));
        vm.MoveSpotsGesture(new(.7, .6), 100);
        await vm.CompleteSpotsGestureAsync();
        var outgoing = vm.SelectedImage!;
        var count = vm.HistoryEntries.Count;
        var incoming = new ImageFile(_fixture.Path("incoming.jpg"));
        incoming.CatalogId = await catalog.GetOrCreateImageAsync(incoming.FilePath);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Content = panel };
        using var scope = new TestUiScope(window);
        var slider = panel.GetVisualDescendants().OfType<SpotsEditSection>().Single()
            .GetVisualDescendants().OfType<CompactSlider>().Single(s => s.Label == "Opacity");
        var wheelClock = new TestTimeProvider();
        slider.WheelTimeProvider = wheelClock;
        var ends = 0;
        slider.DragCompleted += (_, _) => ends++;

        CompactSliderWheelTests.Wheel(slider, 0, -3);
        wheelClock.Advance(TimeSpan.FromMilliseconds(300));
        vm.SelectedImage = incoming;
        Dispatcher.UIThread.RunJobs();
        await vm.PendingHistoryCommitTask!.WaitAsync(TestWaits.Condition);
        await vm.PendingHistoryLoadTask!.WaitAsync(TestWaits.Condition);

        var saved = await catalog.LoadEditHistoryAsync(outgoing.CatalogId);
        Assert.Equal(count + 1, saved.Entries.Count);
        Assert.Equal(.97, saved.Entries[^1].Settings.Repairs![0].Opacity, 8);
        Assert.Equal(.97, outgoing.EditSettings.Repairs![0].Opacity, 8);
        Assert.Equal(1, ends);
        Assert.Equal(0, wheelClock.TimerCount);
        Assert.Empty(vm.HistoryEntries);
        Assert.Null(incoming.EditSettings.Repairs);
        Assert.False(vm.IsSpotsGestureActive);
    }

    [AvaloniaFact]
    public async Task PartialNotchDoesNotCrossImageSelection()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog, false);
        var outgoing = vm.SelectedImage!;
        var incoming = new ImageFile(_fixture.Path("incoming.jpg"));
        incoming.CatalogId = await catalog.GetOrCreateImageAsync(incoming.FilePath);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Content = panel };
        using var scope = new TestUiScope(window);
        var slider = ExposureSlider(panel, false);
        var wheelClock = new TestTimeProvider();
        slider.WheelTimeProvider = wheelClock;
        var starts = 0;
        slider.DragStarted += (_, _) => starts++;

        CompactSliderWheelTests.Wheel(slider, 0, .75);
        vm.SelectedImage = incoming;

        Assert.Equal(0, wheelClock.TimerCount);
        CompactSliderWheelTests.Wheel(slider, 0, .25);
        Assert.Equal(0, slider.Value);
        Assert.Equal(0, starts);

        slider.CompleteWheel();
        await vm.PendingHistoryLoadTask!.WaitAsync(TestWaits.Condition);
        Assert.Empty(vm.HistoryEntries);
        Assert.Empty((await catalog.LoadEditHistoryAsync(outgoing.CatalogId)).Entries);
    }

    [AvaloniaTheory]
    [InlineData("Angle")]
    [InlineData("Center")]
    public async Task CyclicMaximumRoundTripAddsOneRowPerBurst(string label)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog, true);
        await vm.ToggleLocalHueCommand.ExecuteAsync(null);
        vm.IsLocalHueExpanded = true;
        vm.IsLocalGeometryExpanded = true;
        var outgoing = vm.SelectedImage!;
        var incoming = new ImageFile(_fixture.Path("incoming.jpg"));
        incoming.CatalogId = await catalog.GetOrCreateImageAsync(incoming.FilePath);
        var count = vm.HistoryEntries.Count;
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Content = panel };
        using var scope = new TestUiScope(window);
        var slider = panel.GetVisualDescendants().OfType<LocalsEditSection>().Single()
            .GetVisualDescendants().OfType<CompactSlider>().Single(s => s.Label == label);
        slider.WheelTimeProvider = new TestTimeProvider();

        CompactSliderWheelTests.Wheel(slider, 0, 1000);
        slider.CompleteWheel();
        await vm.PendingHistoryCommitTask!.WaitAsync(TestWaits.Condition);

        Assert.Equal(slider.Maximum - slider.SmallChange, slider.Value);
        Assert.Equal(count + 1, vm.HistoryEntries.Count);
        vm.SelectedImage = incoming;
        await vm.PendingHistoryLoadTask!.WaitAsync(TestWaits.Condition);
        outgoing.EditSettings = (await catalog.LoadImageStatesAsync([outgoing.FilePath]))[outgoing.FilePath]
            .Single().EditSettings;
        vm.SelectedImage = outgoing;
        await vm.PendingHistoryLoadTask!.WaitAsync(TestWaits.Condition);

        Assert.Equal(slider.Maximum - slider.SmallChange, slider.Value);
        CompactSliderWheelTests.Wheel(slider, 0, -1);
        slider.CompleteWheel();
        await vm.PendingHistoryCommitTask!.WaitAsync(TestWaits.Condition);

        Assert.Equal(slider.Maximum - 2 * slider.SmallChange, slider.Value);
        Assert.Equal(count + 2, vm.HistoryEntries.Count);
        Assert.Equal(count + 2, (await catalog.LoadEditHistoryAsync(outgoing.CatalogId)).Entries.Count);
    }
}

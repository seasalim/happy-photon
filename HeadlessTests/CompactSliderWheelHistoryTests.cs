using Avalonia;
using Avalonia.Controls;
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

public sealed partial class CompactSliderWheelHistoryTests : IDisposable
{
    private readonly CatalogVmFixture _fixture = new("wheel-history");

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BurstRendersWithoutHistoryThenCommitsOnceAndReleaseStartsANewStep(bool local)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog, local);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 350, Height = 1000, Content = panel };
        using var scope = new TestUiScope(window);
        var slider = ExposureSlider(panel, local);
        slider.WheelTimeProvider = new TestTimeProvider();
        var wheelClock = (TestTimeProvider)slider.WheelTimeProvider;
        var starts = 0;
        var ends = 0;
        slider.DragStarted += (_, _) => starts++;
        slider.DragCompleted += (_, _) => ends++;
        var count = vm.HistoryEntries.Count(entry => entry.Label != "Original");
        Dispatcher.UIThread.RunJobs();
        slider.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        ShowcaseTestHelper.Settle(() =>
        {
            var center = slider.TranslatePoint(new Point(100, 10), window)!.Value;
            var hit = window.InputHitTest(center) as Visual;
            return hit == slider || hit?.GetVisualAncestors().Contains(slider) == true;
        }, "wheel slider hit-test");
        var point = slider.TranslatePoint(new Point(100, 10), window)!.Value;
        window.MouseMove(point);

        for (var notch = 1; notch <= 3; notch++)
        {
            window.MouseWheel(point, new Vector(0, 1), RawInputModifiers.Shift);
            await DrainPreview(vm, clock);

            Assert.Equal(notch * slider.SmallChange, local ? vm.LocalExposure : vm.Exposure, 8);
            Assert.Equal(count, vm.HistoryEntries.Count(entry => entry.Label != "Original"));
        }

        Assert.Equal(1, starts);
        Assert.Equal(0, ends);

        wheelClock.Advance(TimeSpan.FromMilliseconds(300));
        Dispatcher.UIThread.RunJobs();
        await vm.PendingHistoryCommitTask!;

        Assert.Equal(count + 1, vm.HistoryEntries.Count(entry => entry.Label != "Original"));
        Assert.Equal(1, ends);

        CompactSliderWheelTests.Wheel(slider, 0, 1);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.LeftShift });
        CompactSliderWheelTests.Wheel(slider, 0, 1);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.LeftShift });
        await vm.PendingHistoryCommitTask!;

        Assert.Equal(count + 3, vm.HistoryEntries.Count(entry => entry.Label != "Original"));
        Assert.Equal(3, starts);
        Assert.Equal(3, ends);

        await DrainPreview(vm, clock);

        Assert.Equal(count + 3, vm.HistoryEntries.Count(entry => entry.Label != "Original"));

        await vm.UndoCommand.ExecuteAsync(null);

        Assert.Equal(4 * slider.SmallChange, local ? vm.LocalExposure : vm.Exposure, 8);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionCommitsOutgoingImageAndRejectsItsQueuedEnding(bool local)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog, local);
        var outgoing = vm.SelectedImage!;
        var incoming = new ImageFile(_fixture.Path("incoming.jpg"));
        incoming.CatalogId = await catalog.GetOrCreateImageAsync(incoming.FilePath);
        if (local) incoming.EditSettings.Locals = [new LocalAdjustment()];
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 350, Height = 1000, Content = panel };
        using var scope = new TestUiScope(window);
        var slider = ExposureSlider(panel, local);
        var wheelClock = new TestTimeProvider();
        slider.WheelTimeProvider = wheelClock;
        var ends = 0;
        slider.DragCompleted += (_, _) => ends++;

        CompactSliderWheelTests.Wheel(slider, 0, 3);
        wheelClock.Advance(TimeSpan.FromMilliseconds(300));
        vm.SelectedImage = incoming;

        Assert.Equal(1, ends);

        Dispatcher.UIThread.RunJobs();
        await vm.PendingHistoryCommitTask!;
        await vm.PendingHistoryLoadTask!;
        await DrainPreview(vm, clock);

        Assert.Equal(1, ends);
        Assert.Empty(vm.HistoryEntries);
        Assert.Equal(0, incoming.EditSettings.Exposure);

        if (local) Assert.Equal(0, incoming.EditSettings.Locals![0].Exposure);
        else Assert.Null(incoming.EditSettings.Locals);
        var saved = await catalog.LoadEditHistoryAsync(outgoing.CatalogId);

        Assert.Equal(local ? 3 : 2, saved.Entries.Count);

        var last = saved.Entries.OrderBy(entry => entry.Sequence).Last().Settings;

        Assert.Equal(3 * slider.SmallChange, local ? last.Locals![0].Exposure : last.Exposure, 8);

        CompactSliderWheelTests.Wheel(slider, 0, 1);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.RightShift });
        await vm.PendingHistoryCommitTask!;

        Assert.Equal(2, ends);
        Assert.Equal(2, vm.HistoryEntries.Count);
        Assert.Equal(slider.SmallChange, local ? vm.LocalExposure : vm.Exposure, 8);

        vm.SelectedImage = outgoing;
        await vm.PendingHistoryLoadTask!;

        Assert.Equal(local ? 3 : 2, vm.HistoryEntries.Count);
    }

    [AvaloniaTheory]
    [InlineData("Angle")]
    [InlineData("Center")]
    public async Task CyclicLocalsClampUnderWheelAndStillWrapUnderKeys(string label)
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog, true);
        await vm.ToggleLocalHueCommand.ExecuteAsync(null);
        vm.IsLocalHueExpanded = true;
        vm.IsLocalGeometryExpanded = true;
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Width = 350, Height = 1000, Content = panel };
        using var scope = new TestUiScope(window);
        var slider = panel.GetVisualDescendants().OfType<LocalsEditSection>().Single()
            .GetVisualDescendants().OfType<CompactSlider>().Single(control => control.Label == label);
        slider.WheelTimeProvider = new TestTimeProvider();

        CompactSliderWheelTests.Wheel(slider, 0, 1000);

        Assert.Equal(slider.Maximum - slider.SmallChange, slider.Value);

        CompactSliderWheelTests.Wheel(slider, 0, 1);

        Assert.Equal(slider.Maximum - slider.SmallChange, slider.Value);

        CompactSliderWheelTests.Wheel(slider, 0, -1);

        Assert.Equal(slider.Maximum - 2 * slider.SmallChange, slider.Value);

        CompactSliderWheelTests.Wheel(slider, 0, -1000);

        Assert.Equal(slider.Minimum, slider.Value);

        slider.BringIntoView();
        Dispatcher.UIThread.RunJobs();

        Assert.True(slider.Focus());

        window.KeyPress(Key.Left, RawInputModifiers.None, PhysicalKey.None, null);

        Assert.Equal(359, slider.Value);
    }

    [AvaloniaFact]
    public async Task OpenToolLocksGlobalSlidersAndEndsTheirBurst()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog, false);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Content = panel };
        using var scope = new TestUiScope(window);
        var slider = ExposureSlider(panel, false);
        slider.WheelTimeProvider = new TestTimeProvider();
        var ends = 0;
        slider.DragCompleted += (_, _) => ends++;

        CompactSliderWheelTests.Wheel(slider, 0, 1);
        await vm.ToggleLocalsModeCommand.ExecuteAsync(null);

        Assert.False(slider.IsEffectivelyEnabled);
        Assert.Equal(1, ends);

        var value = slider.Value;

        Assert.False(CompactSliderWheelTests.Wheel(slider, 0, 1).Handled);
        Assert.Equal(value, slider.Value);
    }

    [AvaloniaFact]
    public async Task RemovingSliderCommitsItsBurstAndReleasesHistorySuppression()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog, false);
        var panel = new DevelopEditPanel { DataContext = vm };
        var window = new Window { Content = panel };
        using var scope = new TestUiScope(window);
        var slider = ExposureSlider(panel, false);
        slider.WheelTimeProvider = new TestTimeProvider();

        CompactSliderWheelTests.Wheel(slider, 0, 1);
        ((StackPanel)slider.Parent!).Children.Remove(slider);
        await DrainPreview(vm, clock);

        Assert.Equal(2, vm.HistoryEntries.Count);

        vm.Contrast = 1;
        await DrainPreview(vm, clock);

        Assert.Equal(3, vm.HistoryEntries.Count);
    }

    private MainWindowViewModel CreateVm(HappyPhoton.Services.CatalogService catalog, TestTimeProvider clock)
    {
        var vm = _fixture.CreateViewModel(catalog, new LocalTestLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally), timeProvider: clock);
        vm.IsDevelopMode = true;
        vm.ShowWorkspaceReady(MainWindowViewModel.CurrentFirstRunExperienceVersion);

        return vm;
    }

    private async Task Prepare(MainWindowViewModel vm, HappyPhoton.Services.CatalogService catalog, bool local)
    {
        var image = new ImageFile(_fixture.Path("photo.jpg"));
        image.CatalogId = await catalog.GetOrCreateImageAsync(image.FilePath);
        vm.Browse.SetImages([image]);
        vm.SelectedImage = image;

        await TestWaits.UntilAsync(() => vm.IsHistoryLoaded && vm.PreviewImage != null);

        if (local)
        {
            await vm.ToggleLocalsModeCommand.ExecuteAsync(null);
            await vm.PlaceLocalAtCenterCommand.ExecuteAsync(null);
        }
    }

    private static CompactSlider ExposureSlider(DevelopEditPanel panel, bool local) =>
        panel.GetVisualDescendants().OfType<CompactSlider>().Single(slider => slider.Label == "Exposure" &&
            slider.GetVisualAncestors().OfType<LocalsEditSection>().Any() == local);

    private static async Task DrainPreview(MainWindowViewModel vm, TestTimeProvider clock)
    {
        clock.Advance(TimeSpan.FromMilliseconds(200));
        if (vm.PendingPreviewDebounceTask is { } preview) await preview;
    }

    public void Dispose() => _fixture.Dispose();
}


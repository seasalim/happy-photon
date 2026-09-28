using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed partial class SpotsViewModelTests
{
    [AvaloniaFact]
    public async Task VisualizationNeverChangesDocumentHashStatisticsHistoryOrPersistence()
    {
        using var catalog = await _fixture.CreateCatalogAsync();
        var clock = new TestTimeProvider();
        await using var vm = CreateVm(catalog, clock);
        await Prepare(vm, catalog);
        await CreateSpot(vm);
        var image = vm.SelectedImage!;
        var settings = EditSettingsJson.Serialize(image.EditSettings);
        var hash = RenderSettingsHash.Compute(image.EditSettings);
        var histogram = vm.Histogram;
        var history = vm.HistoryEntries.Count;
        var saved = new List<AppSettings>();
        vm.PersistAppSettingsAsync = () =>
        {
            var preferences = new AppSettings();
            vm.CaptureBrushPreferences(preferences);
            saved.Add(preferences);

            return Task.CompletedTask;
        };
        vm.VisualizeSpots = true;
        vm.HideSpotCircles = true;
        clock.Advance(TimeSpan.FromSeconds(1));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(saved);
        Assert.Equal(settings, EditSettingsJson.Serialize(image.EditSettings));
        Assert.Equal(hash, RenderSettingsHash.Compute(image.EditSettings));
        Assert.Same(histogram, vm.Histogram);
        Assert.Equal(history, vm.HistoryEntries.Count);

        var section = new SpotsEditSection { DataContext = vm };
        using var scope = new TestUiScope(new Window { Content = section });
        var threshold = section.GetVisualDescendants().OfType<CompactSlider>().Single(s => s.Label == "Threshold");
        threshold.RaiseEvent(new RoutedEventArgs(CompactSlider.DragStartedEvent));
        threshold.Value = 75;
        threshold.RaiseEvent(new RoutedEventArgs(CompactSlider.DragCompletedEvent));
        Assert.False(vm.IsSpotsGestureActive);
        clock.Advance(TimeSpan.FromMilliseconds(249));
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(saved);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        await TestWaits.UntilAsync(() => saved.Count == 1);
        Assert.Equal(75, saved[0].SpotVisualizeThreshold);
        Assert.Equal(history, vm.HistoryEntries.Count);
        Assert.Equal(settings, EditSettingsJson.Serialize(image.EditSettings));
        Assert.Equal(hash, RenderSettingsHash.Compute(image.EditSettings));
        var service = new AppSettingsService(catalog);
        await service.SavePreferencesAsync(saved[0]);
        vm.RestoreBrushPreferences(await service.LoadAsync());
        Assert.Equal(75, vm.SpotVisualizeThreshold);
        vm.SpotVisualizeThreshold = double.NaN;
        Assert.Equal(75, vm.SpotVisualizeThreshold);
        vm.SpotVisualizeThreshold = 200;
        Assert.Equal(100, vm.SpotVisualizeThreshold);
        await catalog.SetAppSettingsAsync(new Dictionary<string, string?> { ["SpotVisualizeThreshold"] = "NaN" });
        Assert.Equal(50, (await service.LoadAsync()).SpotVisualizeThreshold);
        var persisted = (await catalog.LoadImageStatesAsync([image.FilePath]))[image.FilePath].Single();
        Assert.Equal(settings, EditSettingsJson.Serialize(persisted.EditSettings));
    }
}

using System.Diagnostics;
using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class BrowseSelectionCommandCostGateTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public async Task G4_BaseSelectionToggleAndCanExecute_WhenEnabled()
    {
        PasteGateSupport.RequirePerformance();

        using var fixture = new CatalogVmFixture("browse-selection-command-cost");
        using var catalog = await fixture.CreateCatalogAsync();
        var availability = new TestSourceAvailabilityService(SourceAvailability.AvailableLocally);
        var loader = new PasteGateLoader();
        var metadataCalls = 0;
        await using var vm = fixture.CreateViewModel(catalog, loader, _ =>
        {
            Interlocked.Increment(ref metadataCalls);

            return Task.CompletedTask;
        }, availability, timeProvider: new TestTimeProvider());
        vm.ImageService.Previews.AdjacentWarmEnabled = false;
        // Model resident Browse metadata without reading any original files.
        var photos = Enumerable.Range(0, 10000)
            .Select(index => new ImageFile(fixture.Path($"photo-{index:D5}.dng"))
            {
                MetadataLoaded = true
            })
            .ToArray();
        var source = photos[0];
        source.EditSettings = PasteGateSupport.SourceLook();
        vm.Browse.SetImages(photos);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        vm.SelectAllCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsDevelopMode);
        Assert.False(vm.IsCompareMode);
        Assert.False(vm.IsFullScreenMode);
        Assert.Equal(10000, vm.SelectedCount);
        Assert.True(vm.HasCopiedSettings);
        Assert.True(source.IsSelected);

        var availabilityBefore = availability.CallCount;
        var metadataBefore = Volatile.Read(ref metadataCalls);
        var decodesBefore = loader.Starts;
        var samples = new double[50];
        var timer = new Stopwatch();

        for (var sample = 0; sample < samples.Length; sample++)
        {
            // Each pair deselects and reselects one non-source photo. Keep the
            // same timed body when adding Sync evaluation and its scan check.
            var target = photos[1 + sample / 2];
            var scans = vm.SyncSelectionScanCount;
            timer.Restart();
            vm.ToggleImageSelection(target);
            var enabled = EvaluateCopyPasteCommands(vm);
            timer.Stop();
            samples[sample] = timer.Elapsed.TotalMilliseconds;
            Assert.True(enabled);
            Assert.Equal(scans + 1, vm.SyncSelectionScanCount);
            Assert.Same(source, vm.SelectedImage);
            Assert.True(source.IsSelected);
            Assert.Equal(sample % 2 == 1, target.IsSelected);
            Assert.Equal(sample % 2 == 0 ? 9999 : 10000, vm.SelectedCount);
        }

        Assert.Equal(metadataBefore, Volatile.Read(ref metadataCalls));
        Assert.Equal(decodesBefore, loader.Starts);
        var ordered = samples.Order().ToArray();
        var median = (ordered[24] + ordered[25]) / 2;
        var p90 = ordered[44];
        output.WriteLine(FormattableString.Invariant(
            $"SYNCSETTINGS G4 post median={median:F6} p90={p90:F6}"));
        output.WriteLine($"SYNCSETTINGS G4 pid={Environment.ProcessId} samples=50 photos=10000 " +
            $"availabilityChecks={availability.CallCount - availabilityBefore} metadata=0 decodeStarts=0");
        output.WriteLine("SYNCSETTINGS G4 samples ms=" + string.Join(",",
            samples.Select(value => value.ToString("F6", CultureInfo.InvariantCulture))));
    }

    private static bool EvaluateCopyPasteCommands(MainWindowViewModel vm)
    {
        var copy = vm.CopyEditSettingsCommand.CanExecute(null);
        var paste = vm.PasteEditSettingsCommand.CanExecute(null);
        var choosePaste = vm.ChoosePasteSettingsCommand.CanExecute(null);
        var sync = vm.SyncSettingsCommand.CanExecute(null);

        return copy && paste && choosePaste && sync;
    }
}

using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.Views;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PasteDialogGateTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public Task G3_PasteSettings()
    {
        PasteGateSupport.RequirePerformance();

        return RunAsync(measure: true);
    }

    [AvaloniaFact]
    public Task Sanity_PasteSettings() => RunAsync(measure: false);

    private async Task RunAsync(bool measure)
    {
        using var fixture = new CatalogVmFixture("paste-dialog-gate");
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
        var window = new MainWindow();
        using var scope = TestUiScope.ForMainWindow(window, vm);
        var source = new ImageFile(fixture.Path("source.dng"))
        {
            EditSettings = PasteGateSupport.SourceLook()
        };
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);
        var targets = Enumerable.Range(0, 1000).Select(index => new ImageFile(fixture.Path($"photo-{index}.dng"))
        {
            EditSettings = new EditSettings
            {
                Locals = index % 2 == 0 ? [new LocalAdjustment { Exposure = .5 }] : null,
                Crop = index % 3 == 0 ? new CropRegion { Left = .1, Top = .2, Right = .9, Bottom = .8 } : null
            }
        }).ToArray();
        vm.Browse.SetImages(targets);
        vm.Browse.SelectAllVisible();

        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsDevelopMode);
        Assert.Equal(1000, vm.Browse.SelectedCount);
        Assert.NotNull(vm.ShowPasteSettingsAsync);
        Assert.True(vm.PasteEditSettingsCommand.CanExecute(null));
        // Content readers must first query availability. Zero admission calls,
        // plus zero metadata/loader calls, proves no source access through the
        // production seams. This is a conservative zero-only counter, not an
        // OS read/byte trace; positive values need attribution before reporting reads.
        var availabilityBefore = availability.CallCount;
        var metadataBefore = Volatile.Read(ref metadataCalls);
        var decodesBefore = loader.Starts;
        var shown = new TaskCompletionSource<Window>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = measure ? new Stopwatch() : null;
        Window? dialog = null;
        var sourceAdmissions = -1;
        var sourceMetadata = -1;
        var sourceDecodes = -1;
        // Observe the real window hook rather than replacing confirmation with
        // a no-op delegate. This same boundary can observe WP2's new dialog.
        using var opened = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (ReferenceEquals(window, scope.Window)) return;

            timer?.Stop();
            dialog = window;
            sourceAdmissions = availability.CallCount - availabilityBefore;
            sourceMetadata = Volatile.Read(ref metadataCalls) - metadataBefore;
            sourceDecodes = loader.Starts - decodesBefore;
            shown.TrySetResult(window);
        });
        Task? command = null;

        try
        {
            timer?.Start();
            command = vm.PasteEditSettingsCommand.ExecuteAsync(null);
            var observed = await shown.Task.WaitAsync(TestWaits.Condition);
            Assert.IsType<PasteSettingsDialog>(observed);
            Assert.True(observed.IsVisible);
            Assert.Contains(observed.GetLogicalDescendants().OfType<TextBlock>(),
                text => text.Text == "From source.dng to 1000 photos");
            Assert.Equal(0, sourceAdmissions);
            Assert.Equal(0, sourceMetadata);
            Assert.Equal(0, sourceDecodes);
            output.WriteLine("PASTE_GATE " + JsonSerializer.Serialize(new
            {
                gate = measure ? "G3" : "sanity-dialog",
                fixture = "paste-settings-1000",
                pid = Environment.ProcessId,
                milliseconds = timer?.Elapsed.TotalMilliseconds,
                count = targets.Length,
                sourceReads = sourceAdmissions + sourceMetadata + sourceDecodes,
                sourceAdmissions,
                sourceMetadata,
                sourceDecodes
            }));
        }
        finally
        {
            dialog?.Close(false);

            foreach (var owned in scope.Window!.OwnedWindows.ToArray())
            {
                owned.Close(false);
            }

            if (command is not null)
            {
                await command.WaitAsync(TestWaits.Condition);
            }
        }

        Assert.All(targets, target => Assert.Equal(0, target.EditSettings.Exposure));
    }
}




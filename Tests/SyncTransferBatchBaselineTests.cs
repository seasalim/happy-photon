using System.Diagnostics;
using System.Globalization;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SyncTransferBatchBaselineTests(ITestOutputHelper output)
{
    private const int TargetCount = 500;

    [Fact]
    public async Task BatchWorkloadTransfersDocumentsAndSeedsHistory()
    {
        await RunAsync(measure: false);
    }

    [Fact]
    public Task G2_ConfirmToModelsUpdated_WhenEnabled() => MeasureAsync(sync: false);

    [Fact]
    public Task G2_SyncConfirmToModelsUpdated_WhenEnabled() => MeasureAsync(sync: true);

    [Fact]
    public Task SyncWorkloadTransfersDocumentsAndSeedsHistory() => RunAsync(measure: false, sync: true);

    private async Task MeasureAsync(bool sync)
    {
        Assert.SkipWhen(
            Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 under the exclusive measurement lock.");
#if DEBUG
        Assert.Skip("Run G2 in Release.");
#endif
        var samples = new double[5];

        for (var run = 0; run < samples.Length; run++)
        {
            samples[run] = await RunAsync(measure: true, sync);
            output.WriteLine($"G2 run {run + 1}: {samples[run]:F3} ms");
        }

        var median = samples.Order().ElementAt(2);
        output.WriteLine($"G2 median of 5 runs: {median:F3} ms; targets={TargetCount}");
        var control = Environment.GetEnvironmentVariable("HAPPY_PHOTON_SYNC_CONTROL_MS");

        if (control is null)
        {
            Assert.InRange(median, 25, 400);
        }
        else
        {
            var baseline = double.Parse(control, CultureInfo.InvariantCulture);
            Assert.InRange(baseline, 25, 400);
            Assert.True(median <= baseline * 1.10 + 10,
                $"G2 {median:F3} ms exceeds control {baseline:F3} ms × 1.10 + 10 ms.");
        }
    }

    private static async Task<double> RunAsync(bool measure, bool sync = false)
    {
        using var fixture = new CatalogVmFixture("sync-transfer-batch");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(
            catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: new TestTimeProvider());
        var source = new ImageFile(fixture.Path("LK.dng"))
        {
            EditSettings = CreateLook()
        };
        var targets = new ImageFile[TargetCount];

        for (var index = 0; index < targets.Length; index++)
        {
            var target = new ImageFile(fixture.Path($"target-{index:D3}.dng"));
            target.CatalogId = await catalog.GetOrCreateImageAsync(target.FilePath);
            await catalog.SaveEditSettingsAsync(target.CatalogId, target.EditSettings);
            Assert.Empty((await catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
            targets[index] = target;
        }

        vm.Browse.SetImages([source, .. targets]);
        vm.SelectedImage = source;

        if (sync)
        {
            source.CatalogId = await catalog.GetOrCreateImageAsync(source.FilePath);
            await catalog.SaveEditSettingsAsync(source.CatalogId, source.EditSettings);
            vm.Browse.ToggleSelection(source);
        }
        else
        {
            vm.CopyEditSettingsCommand.Execute(null);
        }

        foreach (var target in targets)
        {
            vm.Browse.ToggleSelection(target);
        }

        if (sync) vm.SelectAllCommand.Execute(null);

        var confirmations = 0;
        var updated = 0;
        var timer = new Stopwatch();
        vm.ShowPasteSettingsAsync = dialog =>
        {
            Assert.Equal(TargetCount, dialog.TargetCount);
            confirmations++;
            if (measure) timer.Start();

            return Task.FromResult(true);
        };

        foreach (var target in targets)
        {
            // HasEdits follows EditSettings assignment. The last notification is
            // before selected-image work and thumbnail refresh in the real command.
            target.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(ImageFile.HasEdits)) return;

                updated++;
                if (updated == TargetCount && measure) timer.Stop();
            };
        }

        Assert.False(vm.IsDevelopMode);
        var command = sync ? vm.SyncSettingsCommand : vm.PasteEditSettingsCommand;
        Assert.True(command.CanExecute(null));
        await command.ExecuteAsync(null);
        Assert.Equal(1, confirmations);
        Assert.Equal(TargetCount, updated);
        Assert.False(timer.IsRunning);
        var elapsed = timer.Elapsed.TotalMilliseconds;
        var expected = EditSettingsJson.Serialize(CreateLook());
        var original = EditSettingsJson.Serialize(new EditSettings());
        var persisted = await catalog.LoadImageStatesAsync(
            targets.Select(target => target.FilePath).ToArray());

        foreach (var target in targets)
        {
            Assert.Equal(expected, EditSettingsJson.Serialize(target.EditSettings));
            Assert.Equal(expected, EditSettingsJson.Serialize(
                Assert.Single(persisted[target.FilePath]).EditSettings));
            var history = await catalog.LoadEditHistoryAsync(target.CatalogId);
            Assert.Equal(1, history.Position);
            Assert.Equal(["Original", "Paste settings"],
                history.Entries.Select(entry => entry.Label));
            Assert.Equal(original, EditSettingsJson.Serialize(history.Entries[0].Settings));
            Assert.Equal(expected, EditSettingsJson.Serialize(history.Entries[1].Settings));
        }

        if (sync)
        {
            Assert.Equal(expected, EditSettingsJson.Serialize(source.EditSettings));
            Assert.Empty((await catalog.LoadEditHistoryAsync(source.CatalogId)).Entries);
            var storedSource = await catalog.LoadImageStatesAsync([source.FilePath]);
            Assert.Equal(expected, EditSettingsJson.Serialize(Assert.Single(storedSource[source.FilePath]).EditSettings));
            Assert.False(vm.HasCopiedSettings);
        }

        return elapsed;
    }

    internal static EditSettings CreateLook()
    {
        var settings = new EditSettings
        {
            Exposure = 0.75,
            Brightness = 11,
            Contrast = 23,
            Highlights = -37,
            Shadows = 29,
            Whites = 17,
            Blacks = -13,
            Saturation = 19,
            Vibrance = 31,
            Wb = new WhiteBalanceSettings { Mode = WbMode.Custom, Kelvin = 7200, Tint = -12 },
            HlReconstruction = HlReconstructionMode.Blend,
            Detail = new DetailSettings { CaptureSharpen = 41, LuminanceNr = 27, ChromaNr = 33 },
            Effects = new EffectsSettings
            {
                Vignette = -21,
                Midpoint = 63,
                Grain = 19,
                GrainSize = GrainSize.Coarse
            },
            Lens = new LensSettings
            {
                Distortion = false,
                ChromaticAberration = false,
                Vignetting = true
            },
            Mixer = new ColorMixerSettings(),
            Curve = Curve(.25, .19),
            CurveRed = Curve(.35, .43),
            CurveGreen = Curve(.45, .39),
            CurveBlue = Curve(.65, .71)
        };

        foreach (var band in Enum.GetValues<ColorMixerBand>())
        {
            var values = settings.Mixer.GetBand(band);
            values.Hue = 12;
            values.Saturation = -18;
            values.Luminance = 24;
        }

        return settings;
    }

    private static CurveData Curve(double x, double y)
    {
        var curve = new CurveData();
        curve.AddPointAndReturnIndex(x, y);

        return curve;
    }
}

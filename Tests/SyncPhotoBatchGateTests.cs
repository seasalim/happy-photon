using System.Diagnostics;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(AvaloniaTestCollection.Name)]
public sealed class SyncPhotoBatchGateTests(ITestOutputHelper output)
{
    private const int TargetCount = 100;

    [Fact]
    public async Task BatchControl()
    {
        SyncPhotoGateSupport.RequirePerformance();
        var elapsed = await RunAsync(measure: true);
        output.WriteLine($"SYNC_PHOTO gate=G3 pid={Environment.ProcessId} milliseconds={elapsed:R}");
    }

    [Fact]
    public async Task LocalsTransfer_AfterImplementation()
    {
        SyncPhotoGateSupport.RequirePerformance();
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_SYNC_PHOTO_TRANSFER") != "1",
            "Enable only at IMPLEMENT: today's dialog excludes Locals.");
        var elapsed = await RunAsync(measure: true, pasteLocals: true);
        output.WriteLine($"SYNC_PHOTO gate=G3 pid={Environment.ProcessId} milliseconds={elapsed:R}");
    }

    private async Task<double> RunAsync(bool measure, bool pasteLocals = false)
    {
        using var fixture = new CatalogVmFixture("sync-transfer-batch");
        using var catalog = await fixture.CreateCatalogAsync();
        await using var vm = fixture.CreateViewModel(
            catalog, new NullBaseLoader(), _ => Task.CompletedTask,
            new TestSourceAvailabilityService(SourceAvailability.AvailableLocally),
            timeProvider: new TestTimeProvider());
        var source = new ImageFile(fixture.Path("LK.dng"))
        {
            EditSettings = CreateSource(pasteLocals)
        };
        if (pasteLocals)
        {
            vm.RestorePasteGroups(EditSettingsTransfer.Groups.ToDictionary(
                group => group.Name, group => group.IsDefault || group.Name == "Locals"));
            Assert.Equal(8, source.EditSettings.Locals!.Count);
        }

        var targets = new ImageFile[TargetCount];

        for (var index = 0; index < targets.Length; index++)
        {
            var target = new ImageFile(fixture.Path($"target-{index:D3}.dng"));
            Assert.Null(target.EditSettings.Locals);
            target.CatalogId = await catalog.GetOrCreateImageAsync(target.FilePath);
            await catalog.SaveEditSettingsAsync(target.CatalogId, target.EditSettings);
            Assert.Empty((await catalog.LoadEditHistoryAsync(target.CatalogId)).Entries);
            targets[index] = target;
        }

        vm.Browse.SetImages([source, .. targets]);
        vm.SelectedImage = source;
        vm.CopyEditSettingsCommand.Execute(null);

        foreach (var target in targets)
        {
            vm.Browse.ToggleSelection(target);
        }

        foreach (var group in EditSettingsTransfer.DefaultGroups)
        {
            var isolated = new EditSettings();
            EditSettingsTransfer.ApplyGroups(source.EditSettings, isolated, [group]);
            Assert.NotEqual(EditSettingsJson.Serialize(new EditSettings()), EditSettingsJson.Serialize(isolated));
        }

        var databasePath = fixture.Path("catalog.db");
        await CheckpointAsync(databasePath);
        var beforeBytes = new FileInfo(databasePath).Length;
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
        Assert.True(vm.PasteEditSettingsCommand.CanExecute(null));
        await vm.PasteEditSettingsCommand.ExecuteAsync(null);
        Assert.Equal(1, confirmations);
        Assert.Equal(TargetCount, updated);
        Assert.False(timer.IsRunning);
        var elapsed = timer.Elapsed.TotalMilliseconds;
        var expected = EditSettingsJson.Serialize(CreateSource(pasteLocals));
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

        await CheckpointAsync(databasePath);
        var afterBytes = new FileInfo(databasePath).Length;
        if (pasteLocals) Assert.True(afterBytes - beforeBytes <= 8554480, "G2 growth limit");
        output.WriteLine($"SYNC_PHOTO gate=G2 pid={Environment.ProcessId} targets={TargetCount} pasteLocals={pasteLocals} beforeBytes={beforeBytes} afterBytes={afterBytes} growthBytes={afterBytes - beforeBytes} checkpoint=TRUNCATE documents=100 historySteps=100");

        return elapsed;
    }

    private static async Task CheckpointAsync(string path)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
    }

    private static EditSettings CreateSource(bool pasteLocals)
    {
        var settings = CreateLook();

        if (pasteLocals)
        {
            settings.Locals = SyncPhotoBrushFixture.Create().Locals;
        }

        return settings;
    }

    internal static EditSettings CreateLook()
    {
        var settings = new EditSettings
        {
            Texture = 17,
            Clarity = -13,
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


using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class PresetServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonPresetTests_{Guid.NewGuid():N}");

    [Fact]
    public async Task Save_CreatesRoundTrippableJsonWithSettingsAndCurve()
    {
        var service = await CreateServiceAsync();
        var source = CreateSettings();

        var preset = await service.SaveUserPresetAsync("Sunset Warm", source);

        Assert.StartsWith("user_", preset.Id);
        var path = Path.Combine(_tempDirectory, $"{preset.Id}.json");
        Assert.True(File.Exists(path));
        var file = JsonSerializer.Deserialize<UserPresetFile>(await File.ReadAllTextAsync(path));
        Assert.NotNull(file);
        Assert.Equal(UserPresetFile.CurrentVersion, file.Version);
        Assert.Equal(EditSettings.CurrentVersion, file.Settings.Version);
        Assert.Equal("Sunset Warm", file.Name);
        Assert.Equal(source.Exposure, file.Settings.Exposure);
        Assert.Equal(source.Wb.Mode, file.Settings.Wb.Mode);
        Assert.Equal(source.Wb.Kelvin, file.Settings.Wb.Kelvin);
        Assert.Equal(source.Wb.Tint, file.Settings.Wb.Tint);
        Assert.Equal(source.Curve.Points.Count, file.Settings.Curve.Points.Count);
        Assert.Equal(source.Curve.Points[1].Y, file.Settings.Curve.Points[1].Y);
        Assert.Equal(source.CurveRed!.Points[1].Y, file.Settings.CurveRed!.Points[1].Y);
    }

    [Fact]
    public async Task Save_RoundTripsV2ColorAndDecodeSettings()
    {
        var service = await CreateServiceAsync();
        var source = new EditSettings
        {
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = 7200,
                Tint = -15
            },
            BaseLook = true,
            HlReconstruction = HlReconstructionMode.Blend
        };

        var preset = await service.SaveUserPresetAsync("V2", source);
        var reloaded = new PresetService(_tempDirectory);
        await reloaded.InitializeAsync();
        var loaded = Assert.Single(reloaded.UserPresets);
        var settings = loaded.Settings;

        Assert.Equal(preset.Id, loaded.Id);
        Assert.Equal(WbMode.Custom, settings.Wb.Mode);
        Assert.Equal(7200, settings.Wb.Kelvin);
        Assert.Equal(-15, settings.Wb.Tint);
        Assert.True(settings.BaseLook);
        Assert.Equal(HlReconstructionMode.Blend, settings.HlReconstruction);
    }

    [Fact]
    public async Task Save_RoundTripsActiveEffectsAsDeepClone()
    {
        var service = await CreateServiceAsync();
        var source = new EditSettings
        {
            Effects = new EffectsSettings
            {
                Vignette = -44,
                Midpoint = 72,
                Grain = 31,
                GrainSize = GrainSize.Coarse
            }
        };

        var saved = await service.SaveUserPresetAsync("Effects", source);
        source.Effects!.Grain = 1;
        var reloaded = new PresetService(_tempDirectory);
        await reloaded.InitializeAsync();
        var effects = Assert.Single(reloaded.UserPresets).Settings.Effects;

        Assert.NotSame(source.Effects, saved.Settings.Effects);
        Assert.Equal(-44, effects!.Vignette);
        Assert.Equal(72, effects.Midpoint);
        Assert.Equal(31, effects.Grain);
        Assert.Equal(GrainSize.Coarse, effects.GrainSize);
    }

    [Fact]
    public async Task Save_CanonicalizesInactiveEffects()
    {
        var service = await CreateServiceAsync();
        var preset = await service.SaveUserPresetAsync(
            "Inactive Effects",
            new EditSettings
            {
                Effects = new EffectsSettings
                {
                    Midpoint = 91,
                    GrainSize = GrainSize.Coarse
                }
            });

        Assert.Null(preset.Settings.Effects);
        Assert.DoesNotContain(
            "\"effects\"",
            await File.ReadAllTextAsync(Path.Combine(
                _tempDirectory,
                $"{preset.Id}.json")));
    }

    [Fact]
    public async Task Save_RoundTripsActiveMixerAndCanonicalizesIdentity()
    {
        var service = await CreateServiceAsync();
        var source = new EditSettings { Mixer = new ColorMixerSettings() };
        source.Mixer.Yellow.Hue = -24;
        source.Mixer.Aqua.Saturation = 38;
        source.Mixer.Purple.Luminance = -17;

        var active = await service.SaveUserPresetAsync("Mixer", source);
        source.Mixer.Aqua.Saturation = 1;
        var identity = await service.SaveUserPresetAsync(
            "Identity Mixer",
            new EditSettings { Mixer = new ColorMixerSettings() });
        var reloaded = new PresetService(_tempDirectory);
        await reloaded.InitializeAsync();
        var loaded = reloaded.GetById(active.Id)!.Settings.Mixer;

        Assert.NotSame(source.Mixer, active.Settings.Mixer);
        Assert.Equal(-24, loaded!.Yellow.Hue);
        Assert.Equal(38, loaded.Aqua.Saturation);
        Assert.Equal(-17, loaded.Purple.Luminance);
        Assert.Null(reloaded.GetById(identity.Id)!.Settings.Mixer);
        Assert.DoesNotContain(
            "\"mixer\"",
            await File.ReadAllTextAsync(Path.Combine(
                _tempDirectory,
                $"{identity.Id}.json")));
    }

    [Fact]
    public async Task Save_ZeroesGeometryAndAppliedPresetId()
    {
        var service = await CreateServiceAsync();
        var source = CreateSettings();
        source.Rotation = 90;
        source.HorizonRotation = 2.5;
        source.Geometry = new GeometrySettings
        {
            Vertical = 20,
            Horizontal = -30,
            Aspect = 40,
            Distortion = -50
        };
        source.Crop = new CropRegion { Left = 0.1, Top = 0.2, Right = 0.8, Bottom = 0.9 };
        source.AppliedPresetId = "user_existing";
        source.RawProfile = new RawProfileSelection
        {
            Source = RawProfileSource.UserFile,
            Location = "synthetic.dcp",
            ContentHash = new string('d', 64)
        };

        var preset = await service.SaveUserPresetAsync("No Geometry", source);

        Assert.Equal(0, preset.Settings.Rotation);
        Assert.Equal(0, preset.Settings.HorizonRotation);
        Assert.Null(preset.Settings.Crop);
        Assert.Null(preset.Settings.Geometry);
        Assert.Null(preset.Settings.AppliedPresetId);
        Assert.Null(preset.Settings.RawProfile);
        Assert.Equal(90, source.Rotation);
        Assert.NotNull(source.Crop);
    }

    [Fact]
    public async Task Save_WithOverwriteIdKeepsIdAndReplacesNameAndSettings()
    {
        var service = await CreateServiceAsync();
        var original = await service.SaveUserPresetAsync("Original", CreateSettings());
        var replacement = new EditSettings { Exposure = -1.25, Contrast = 42 };

        var overwritten = await service.SaveUserPresetAsync("Replacement", replacement, original.Id);

        Assert.Equal(original.Id, overwritten.Id);
        Assert.Single(Directory.GetFiles(_tempDirectory, "*.json"));
        Assert.Equal("Replacement", service.GetById(original.Id)?.Name);
        Assert.Equal(-1.25, service.GetById(original.Id)?.Settings.Exposure);
        Assert.Equal(42, service.GetById(original.Id)?.Settings.Contrast);
    }

    [Fact]
    public async Task Initialize_LoadsOnlyUserPresetsSortedByName()
    {
        var writer = await CreateServiceAsync();
        await writer.SaveUserPresetAsync("Zulu", new EditSettings());
        await writer.SaveUserPresetAsync("alpha", new EditSettings());
        var service = new PresetService(_tempDirectory);

        await service.InitializeAsync();

        Assert.Equal(2, service.AllPresets.Count);
        Assert.Equal(new[] { "alpha", "Zulu" }, service.AllPresets.Select(preset => preset.Name));
        Assert.Equal(new[] { "alpha", "Zulu" }, service.UserPresets.Select(preset => preset.Name));
    }

    [Fact]
    public async Task Mutations_PublishSortedStableReadOnlySnapshots()
    {
        var service = await CreateServiceAsync();
        var observedNames = new List<string[]>();
        service.PresetsChanged += (_, _) => observedNames.Add(
            service.UserPresets.Select(preset => preset.Name).ToArray());

        var zulu = await service.SaveUserPresetAsync("Zulu", new EditSettings());
        var retainedAfterFirstSave = service.UserPresets;
        var alpha = await service.SaveUserPresetAsync("alpha", new EditSettings());

        Assert.Equal(
            new[] { "alpha", "Zulu" },
            service.UserPresets.Select(preset => preset.Name));
        Assert.Equal(
            new[] { "Zulu" },
            retainedAfterFirstSave.Select(preset => preset.Name));

        await service.RenameUserPresetAsync(zulu.Id, "Aardvark");
        var retainedBeforeDelete = service.UserPresets;
        await service.DeleteUserPresetAsync(alpha.Id);

        Assert.Equal(
            new[] { "Aardvark" },
            service.UserPresets.Select(preset => preset.Name));
        Assert.Equal(
            new[] { "Aardvark", "alpha" },
            retainedBeforeDelete.Select(preset => preset.Name));
        Assert.Collection(
            observedNames,
            names => Assert.Equal(new[] { "Zulu" }, names),
            names => Assert.Equal(new[] { "alpha", "Zulu" }, names),
            names => Assert.Equal(new[] { "Aardvark", "alpha" }, names),
            names => Assert.Equal(new[] { "Aardvark" }, names));
        Assert.True(((IList<Preset>)service.UserPresets).IsReadOnly);
    }

    [Fact]
    public async Task Initialize_SkipsCorruptFileAndLoadsRemainingPresets()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(Path.Combine(_tempDirectory, "broken.json"), "{not json");
        var writer = new PresetService(_tempDirectory);
        await writer.InitializeAsync();
        var valid = await writer.SaveUserPresetAsync("Valid", new EditSettings());
        var service = new PresetService(_tempDirectory);

        var exception = await Record.ExceptionAsync(service.InitializeAsync);

        Assert.Null(exception);
        Assert.Single(service.UserPresets);
        Assert.Equal(valid.Id, service.UserPresets[0].Id);
    }

    [Fact]
    public async Task Initialize_SkipsMalformedPropertyTypeAndLoadsRemainingPresets()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_tempDirectory, "malformed.json"),
            """{"version":2,"id":42,"name":"Bad","settings":{"version":2}}""");
        var writer = new PresetService(_tempDirectory);
        await writer.InitializeAsync();
        var valid = await writer.SaveUserPresetAsync("Valid", new EditSettings());
        var service = new PresetService(_tempDirectory);

        var exception = await Record.ExceptionAsync(service.InitializeAsync);

        Assert.Null(exception);
        Assert.Single(service.UserPresets);
        Assert.Equal(valid.Id, service.UserPresets[0].Id);
    }

    [Fact]
    public async Task Initialize_UsesIdentityCurveWhenJsonCurveIsNull()
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "user_null_curve.json");
        await File.WriteAllTextAsync(path,
            """{"version":2,"id":"user_null_curve","name":"Null Curve","settings":{"version":3,"lens":{"distortion":true,"chromaticAberration":true,"vignetting":false,"baseline":"standard"},"curve":null}}""");
        var service = new PresetService(_tempDirectory);

        var exception = await Record.ExceptionAsync(service.InitializeAsync);

        Assert.Null(exception);
        Assert.True(Assert.Single(service.UserPresets).Settings.Curve.IsIdentity());
    }

    [Fact]
    public async Task Initialize_MigratesV012SettingsWithoutRewritingPreset()
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "user_v012.json");
        var json =
            """{"version":2,"id":"user_v012","name":"0.1.2 Preset","settings":{"version":2,"exposure":0.75,"contrast":12}}""";
        await File.WriteAllTextAsync(path, json);
        var service = new PresetService(_tempDirectory);

        await service.InitializeAsync();

        var settings = Assert.Single(service.UserPresets).Settings;
        Assert.Equal(EditSettings.CurrentVersion, settings.Version);
        Assert.Equal(0.75, settings.Exposure);
        Assert.Equal(12, settings.Contrast);
        Assert.Equal(LensBaseline.Legacy, settings.Lens.Baseline);
        Assert.False(settings.Lens.Distortion);
        Assert.False(settings.Lens.ChromaticAberration);
        Assert.False(settings.Lens.Vignetting);
        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Initialize_SkipsPresetWithoutExplicitVersions()
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_tempDirectory, "missing_file_version.json"),
            """{"id":"missing_file_version","name":"Missing","settings":{"version":2}}""");
        await File.WriteAllTextAsync(
            Path.Combine(_tempDirectory, "missing_settings_version.json"),
            """{"version":2,"id":"missing_settings_version","name":"Missing","settings":{}}""");
        var service = new PresetService(_tempDirectory);

        await service.InitializeAsync();

        Assert.Empty(service.UserPresets);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(2, 99)]
    public async Task Initialize_SkipsUnsupportedFileOrSettingsVersion(
        int fileVersion,
        int settingsVersion)
    {
        Directory.CreateDirectory(_tempDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_tempDirectory, "user_future.json"),
            $$"""
              {
                "version": {{fileVersion}},
                "id": "user_future",
                "name": "Future",
                "settings": { "version": {{settingsVersion}} }
              }
              """);
        var service = new PresetService(_tempDirectory);

        await service.InitializeAsync();

        Assert.Empty(service.UserPresets);
    }

    [Fact]
    public async Task Rename_PreservesIdAndFileNameAndUpdatesLookup()
    {
        var service = await CreateServiceAsync();
        var preset = await service.SaveUserPresetAsync("Before", CreateSettings());
        var path = Path.Combine(_tempDirectory, $"{preset.Id}.json");

        await service.RenameUserPresetAsync(preset.Id, "After");

        Assert.True(File.Exists(path));
        Assert.Single(Directory.GetFiles(_tempDirectory, "*.json"));
        Assert.Equal("After", service.GetById(preset.Id)?.Name);
    }

    [Fact]
    public async Task Delete_RemovesFileAndEntryAndSecondDeleteIsNoOp()
    {
        var service = await CreateServiceAsync();
        var preset = await service.SaveUserPresetAsync("Temporary", CreateSettings());
        var path = Path.Combine(_tempDirectory, $"{preset.Id}.json");

        await service.DeleteUserPresetAsync(preset.Id);
        await service.DeleteUserPresetAsync(preset.Id);

        Assert.False(File.Exists(path));
        Assert.Null(service.GetById(preset.Id));
    }

    [Fact]
    public async Task FindUserPresetByName_IsCaseInsensitive()
    {
        var service = await CreateServiceAsync();
        var preset = await service.SaveUserPresetAsync("My Warm Look", CreateSettings());

        Assert.Equal(preset.Id, service.FindUserPresetByName("my WARM look")?.Id);
        Assert.Null(service.FindUserPresetByName("Soft"));
    }

    [Fact]
    public async Task PresetsChanged_FiresAfterInitializeSaveRenameAndDelete()
    {
        var service = new PresetService(_tempDirectory);
        var changeCount = 0;
        service.PresetsChanged += (_, _) => changeCount++;

        await service.InitializeAsync();
        var preset = await service.SaveUserPresetAsync("One", CreateSettings());
        await service.RenameUserPresetAsync(preset.Id, "Two");
        await service.DeleteUserPresetAsync(preset.Id);

        Assert.Equal(4, changeCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private async Task<PresetService> CreateServiceAsync()
    {
        var service = new PresetService(_tempDirectory);
        await service.InitializeAsync();
        return service;
    }

    private static EditSettings CreateSettings()
    {
        var curve = new CurveData();
        curve.Points.Insert(1, new CurvePoint(0.5, 0.65));
        curve.BuildLookupTable();
        var settings = TestEditSettingsFactory.CreateTonal(
            exposure: 0.75,
            brightness: 4,
            contrast: 12,
            saturation: 9,
            vibrance: 17,
            shadows: 11,
            highlights: -21,
            curve: curve);
        settings.Wb = new WhiteBalanceSettings
        {
            Mode = WbMode.Custom,
            Kelvin = 6800,
            Tint = 8
        };
        settings.CurveRed = curve.Clone();
        return settings;
    }
}

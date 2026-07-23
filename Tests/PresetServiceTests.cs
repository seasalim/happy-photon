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
        Assert.Equal("Sunset Warm", file.Name);
        Assert.Equal(source.Exposure, file.Settings.Exposure);
        Assert.Equal(source.Temperature, file.Settings.Temperature);
        Assert.Equal(source.Curve.Points.Count, file.Settings.Curve.Points.Count);
        Assert.Equal(source.Curve.Points[1].Y, file.Settings.Curve.Points[1].Y);
    }

    [Fact]
    public async Task Save_ZeroesGeometryAndAppliedPresetId()
    {
        var service = await CreateServiceAsync();
        var source = CreateSettings();
        source.Rotation = 90;
        source.HorizonRotation = 2.5;
        source.Crop = new CropRegion { Left = 0.1, Top = 0.2, Right = 0.8, Bottom = 0.9 };
        source.AppliedPresetId = "user_existing";

        var preset = await service.SaveUserPresetAsync("No Geometry", source);

        Assert.Equal(0, preset.Settings.Rotation);
        Assert.Equal(0, preset.Settings.HorizonRotation);
        Assert.Null(preset.Settings.Crop);
        Assert.Null(preset.Settings.AppliedPresetId);
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
    public async Task Initialize_UsesIdentityCurveWhenJsonCurveIsNull()
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "user_null_curve.json");
        await File.WriteAllTextAsync(path,
            """{"version":1,"id":"user_null_curve","name":"Null Curve","settings":{"curve":null}}""");
        var service = new PresetService(_tempDirectory);

        var exception = await Record.ExceptionAsync(service.InitializeAsync);

        Assert.Null(exception);
        Assert.True(Assert.Single(service.UserPresets).Settings.Curve.IsIdentity());
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
        return new EditSettings
        {
            Exposure = 0.75,
            Temperature = 18,
            Brightness = 4,
            Contrast = 12,
            Saturation = 9,
            Vibrance = 17,
            Shadows = 11,
            Highlights = -21,
            Curve = curve
        };
    }
}

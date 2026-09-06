using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LocalPersistenceTests
{
    [Fact]
    public void MigrationAndCanonicalEmptyPreserveGlobalMeaning()
    {
        var source = new EditSettings { Exposure = 1.25, Locals = [] };
        var json = EditSettingsJson.Serialize(source);
        Assert.DoesNotContain("locals", json);
        var old = json.Replace("\"version\":4", "\"version\":3");
        var migrated = EditSettingsJson.Deserialize(old, out var clamped);
        Assert.False(clamped);
        Assert.Equal(4, migrated.Version);
        Assert.True(source.HasSameEdits(migrated));
        Assert.Equal(json, EditSettingsJson.Serialize(migrated));
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(
            old.Replace("\"version\":3", "\"version\":2"), out _));
    }

    [Fact]
    public void DisabledAndNeutralLocalsPersistAndCloneIndependently()
    {
        var source = new EditSettings { Locals = [new() { Enabled = false }] };
        var copy = source.Clone();
        Assert.True(source.HasEdits);
        Assert.True(source.HasSameEdits(copy));
        copy.Locals![0].Exposure = 2;
        Assert.False(source.HasSameEdits(copy));
        Assert.Equal(0, source.Locals![0].Exposure);
        Assert.Equal(source.Locals[0].Id, copy.Locals[0].Id);
        var loaded = EditSettingsJson.Deserialize(EditSettingsJson.Serialize(source), out _);
        Assert.True(source.HasSameEdits(loaded));
        Assert.Equal("Linear 1 exposure +2.00 (+2.00)", EditHistoryLabel.Derive(source, copy));
    }

    [Fact]
    public void BoundsClampAndInvalidDocumentsReject()
    {
        var source = new EditSettings { Locals = [new() { Cu = -3, Cv = 4,
            Angle = -90, Feather = 0, Exposure = 10 }] };
        var loaded = EditSettingsJson.Deserialize(JsonSerializer.Serialize(source), out var clamped);
        Assert.True(clamped);
        Assert.Equal((-1d, 2d, 270d, .001, 4d), (loaded.Locals![0].Cu,
            loaded.Locals[0].Cv, loaded.Locals[0].Angle, loaded.Locals[0].Feather,
            loaded.Locals[0].Exposure));
        source.Locals = Enumerable.Range(1, 9).Select(i => new LocalAdjustment { Ordinal = i }).ToList();
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(JsonSerializer.Serialize(source), out _));
        source.Locals = [new() { Type = "unknown" }];
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(JsonSerializer.Serialize(source), out _));
        source.Locals = [new() { Angle = double.NaN }];
        Assert.Throws<JsonException>(() => EditSettingsJson.Serialize(source));
    }

    [Fact]
    public void RadialsRoundTripClampPerTypeAndCarryWithRotation()
    {
        var radial = new LocalAdjustment { Type = "radial", Ordinal = 2, Rx = -1, Ry = 3,
            Feather = -1, Outside = true, Cu = .2, Cv = .3, Angle = 30 };
        var settings = new EditSettings { Locals = [new() { Feather = 0 }, radial] };
        var loaded = EditSettingsJson.Deserialize(JsonSerializer.Serialize(settings), out var clamped);
        Assert.True(clamped);
        Assert.Equal(.001, loaded.Locals![0].Feather);
        var local = loaded.Locals[1];
        Assert.Equal((.001, 1d, 0d, true), (local.Rx, local.Ry, local.Feather, local.Outside));
        Assert.Equal("Radial 2", local.Name);
        var canonical = EditSettingsJson.Serialize(loaded);
        Assert.True(loaded.HasSameEdits(EditSettingsJson.Deserialize(canonical, out _)));
        using var json = JsonDocument.Parse(canonical);
        Assert.False(json.RootElement.GetProperty("locals")[0].TryGetProperty("rx", out _));
        Assert.True(json.RootElement.GetProperty("locals")[1].GetProperty("outside").GetBoolean());
        var before = local with { };
        local.Rotate(90);
        Assert.Equal(.7, local.Cu, 12);
        Assert.Equal(.2, local.Cv, 12);
        Assert.Equal(120, local.Angle);
        Assert.Equal((before.Rx, before.Ry, before.Feather, before.Outside),
            (local.Rx, local.Ry, local.Feather, local.Outside));
        local.Rx = double.NaN;
        Assert.Throws<JsonException>(() => EditSettingsJson.Serialize(loaded));
    }

    [Fact]
    public async Task PresetsStripOnSaveAndIgnoreHandEditedLocalsOnLoad()
    {
        using var directory = new TemporaryDirectory();
        var service = new PresetService(directory.Path);
        await service.InitializeAsync();
        var source = new EditSettings { Exposure = 1, Locals = [new()] };
        var preset = await service.SaveUserPresetAsync("Local exclusion", source);
        Assert.Null(preset.Settings.Locals);
        var path = Path.Combine(directory.Path, preset.Id + ".json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        node["settings"]!["locals"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"type\":\"unknown\"}]");
        node["settings"]!["version"] = 3;
        await File.WriteAllTextAsync(path, node.ToJsonString());
        var loaded = new PresetService(directory.Path);
        await loaded.InitializeAsync();
        Assert.Equal(4, Assert.Single(loaded.UserPresets).Settings.Version);
        Assert.Null(loaded.UserPresets[0].Settings.Locals);
        var destination = new EditSettings { Locals = [new() { Exposure = -1 }] };
        var id = destination.Locals[0].Id;
        EditSettingsTransfer.ApplySubset(source, destination);
        Assert.Equal(id, destination.Locals![0].Id);
        Assert.Equal(-1, destination.Locals[0].Exposure);
        Assert.Null(EditSettingsTransfer.CopySubset(source).Locals);
    }

    [Theory]
    [InlineData(90, .7, .2, 120)]
    [InlineData(-90, .3, .8, 300)]
    [InlineData(180, .8, .7, 210)]
    public void QuarterTurnsCarryGeometry(int degrees, double u, double v, double angle)
    {
        var local = new LocalAdjustment { Cu = .2, Cv = .3, Angle = 30 };
        local.Rotate(degrees);
        Assert.Equal(u, local.Cu, 12);
        Assert.Equal(v, local.Cv, 12);
        Assert.Equal(angle, local.Angle);
        local.Rotate(-degrees);
        Assert.Equal(.2, local.Cu, 12);
        Assert.Equal(.3, local.Cv, 12);
        Assert.Equal(30, local.Angle);
        Assert.Equal(.25, local.Feather);
    }
}

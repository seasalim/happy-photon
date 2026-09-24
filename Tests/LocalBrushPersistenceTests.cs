using System.Text.Json;
using System.Text.Json.Nodes;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LocalBrushPersistenceTests
{
    private static EditSettings Settings() => new() { Locals = [new() { Type = "brush", Exposure = 1,
        Strokes = [new() { Points = [new(4096, 8192), new(5000, 9000)] }] }] };
    private static JsonObject Document() => JsonNode.Parse(EditSettingsJson.Serialize(Settings()))!.AsObject();

    [Fact]
    public void RoundtripUsesDeltaIntegersAndNoGradientGeometry()
    {
        var settings = Settings();
        var json = EditSettingsJson.Serialize(settings);
        var loaded = EditSettingsJson.Deserialize(json, out var changed);
        Assert.False(changed);
        Assert.True(settings.HasSameEdits(loaded));
        Assert.Equal(json, EditSettingsJson.Serialize(loaded));
        Assert.Equal("Brush 1", loaded.Locals![0].Name);
        var local = JsonNode.Parse(json)!["locals"]![0]!;
        Assert.Equal(new[] { 4096, 8192, 904, 808 }, local["strokes"]![0]!["points"]!.AsArray().Select(p => (int)p!));
        foreach (var key in new[] { "cu", "cv", "angle", "feather", "rx", "ry", "outside" }) Assert.Null(local[key]);
        Assert.Equal(4, (int)JsonNode.Parse(json)!["version"]!);
    }

    [Fact]
    public void LoadedClampsReportChangesAndAccumulateUnclampedDeltas()
    {
        var doc = Document(); var stroke = doc["locals"]![0]!["strokes"]![0]!;
        stroke["radius"] = 2; stroke["feather"] = -1; stroke["flow"] = 0;
        stroke["points"] = new JsonArray(-32768, 49152, 32768, -32768, int.MaxValue, int.MinValue, -int.MaxValue, int.MaxValue);
        stroke["futurePressure"] = new JsonArray(1, 1, 1, 1);
        var loaded = EditSettingsJson.Deserialize(doc.ToJsonString(), out var changed);
        Assert.True(changed);
        var actual = loaded.Locals![0].Strokes![0];
        Assert.Equal((.25, 0d, .05), (actual.Radius, actual.Feather, actual.Flow));
        Assert.Equal(new[] { new LocalBrushPoint(-16384, 32768), new(0, 16384), new(32768, -16384), new(0, 16383) }, actual.Points);
        var canonical = EditSettingsJson.Serialize(loaded);
        Assert.Equal(canonical, EditSettingsJson.Serialize(EditSettingsJson.Deserialize(canonical, out changed)));
        Assert.False(changed);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("""[{"mode":"paint","radius":0.03,"feather":0.5,"flow":1,"points":[]}]""")]
    public void RejectsMalformedStrokeLists(string strokes)
    {
        var doc = Document(); doc["locals"]![0]!["strokes"] = JsonNode.Parse(strokes);
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(doc.ToJsonString(), out _));
    }

    [Theory]
    [InlineData("mode", "\"other\"")]
    [InlineData("points", "[1]")]
    [InlineData("points", "[0,0,0.1,0]")]
    [InlineData("points", "[0,0,2147483648,0]")]
    [InlineData("points", "[0,0,null,0]")]
    [InlineData("points", "null")]
    [InlineData("radius", "\"NaN\"")]
    public void RejectsMalformedStrokeFields(string key, string value)
    {
        var doc = Document(); doc["locals"]![0]!["strokes"]![0]![key] = JsonNode.Parse(value);
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(doc.ToJsonString(), out _));
    }

    [Fact]
    public void RequiredFieldsUnknownTypesAndEmptyBrush()
    {
        foreach (var key in new[] { "mode", "radius", "feather", "flow", "points" })
        {
            var doc = Document(); doc["locals"]![0]!["strokes"]![0]!.AsObject().Remove(key);
            Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(doc.ToJsonString(), out _));
        }
        var missing = Document(); missing["locals"]![0]!.AsObject().Remove("strokes");
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(missing.ToJsonString(), out _));
        var unknown = Document(); unknown["locals"]![0]!["type"] = "future-brush";
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(unknown.ToJsonString(), out _));
        var empty = Settings(); empty.Locals![0].Strokes = [];
        Assert.Empty(EditSettingsJson.Deserialize(EditSettingsJson.Serialize(empty), out _).Locals![0].Strokes!);
    }

    [Fact]
    public void CapsRejectAtLoadAcrossBrushesIncludingDisabledAndNeutral()
    {
        var settings = new EditSettings { Locals = Enumerable.Range(0, 8).Select(i => new LocalAdjustment
        {
            Type = "brush", Ordinal = i + 1, Enabled = false,
            Strokes = Enumerable.Range(0, 12).Select(s => new LocalBrushStroke
            { Points = new LocalBrushPoint[41 + (i * 12 + s < 64 ? 1 : 0)] }).ToArray()
        }).ToList() };
        var json = EditSettingsJson.Serialize(settings);
        var loaded = EditSettingsJson.Deserialize(json, out var changed);
        Assert.False(changed);
        Assert.Equal(4000, loaded.Locals!.Sum(l => l.Strokes!.Sum(s => s.Points.Length)));
        var doc = JsonNode.Parse(json)!;
        doc["locals"]![0]!["strokes"]![0]!["points"]!.AsArray().Add(0);
        doc["locals"]![0]!["strokes"]![0]!["points"]!.AsArray().Add(0);
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(doc.ToJsonString(), out _));
        doc = JsonNode.Parse(json)!;
        doc["locals"]![0]!["strokes"]!.AsArray().Add(Document()["locals"]![0]!["strokes"]![0]!.DeepClone());
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(doc.ToJsonString(), out _));
    }

    [Fact]
    public async Task VersionDuplicationPreservesAndIsolatesStrokes()
    {
        using var directory = new TemporaryDirectory();
        using var catalog = new CatalogService(directory.Path);
        await catalog.InitializeAsync();
        var path = Path.Combine(directory.Path, "metadata-only.jpg");
        var id = await catalog.GetOrCreateImageAsync(path);
        var original = Settings();
        await catalog.SaveEditSettingsAsync(id, original);
        var duplicate = (await catalog.CreateVersionAsync(id))!;
        Assert.True(original.HasSameEdits(duplicate.EditSettings));
        duplicate.EditSettings.Locals![0].Rotate(90);
        await catalog.SaveEditSettingsAsync(duplicate.CatalogId, duplicate.EditSettings);
        var versions = (await catalog.LoadImageStatesAsync([path]))[path];
        Assert.True(original.HasSameEdits(versions[0].EditSettings));
        Assert.False(original.HasSameEdits(versions[1].EditSettings));
    }

    [Fact]
    public void WithCloneEqualityAndRotationShareImmutableStrokeArrays()
    {
        var settings = Settings(); var original = settings.Locals![0];
        var copy = original with { }; var clone = settings.Clone();
        Assert.Equal(original, copy); Assert.Equal(original.GetHashCode(), copy.GetHashCode());
        Assert.Same(original.Strokes, copy.Strokes);
        Assert.Same(original.Strokes![0], copy.Strokes![0]);
        Assert.Same(original.Strokes[0].Points, copy.Strokes[0].Points);
        copy.Rotate(90);
        Assert.Equal(new LocalBrushPoint(8192, 4096), copy.Strokes![0].Points[0]);
        var rotated = new EditSettings { Locals = [copy] };
        Assert.True(rotated.HasSameEdits(EditSettingsJson.Deserialize(EditSettingsJson.Serialize(rotated), out _)));
        copy.Rotate(-90); Assert.Equal(original, copy);
        for (var i = 0; i < 4; i++) copy.Rotate(90);
        Assert.Equal(original, copy);
        clone.Locals![0].Strokes = [original.Strokes[0] with { Points = [new(0, 0)] }];
        Assert.False(settings.HasSameEdits(clone));
        Assert.Equal(new LocalBrushPoint(4096, 8192), original.Strokes![0].Points[0]);
        copy.Strokes = [copy.Strokes![0] with { Flow = .5 }]; Assert.NotEqual(original, copy);
        Assert.Equal(1, original.Strokes[0].Flow);
        Assert.NotEqual(original, original with { Cu = .2 });
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("radial")]
    public void GradientWithStrokesIsRejected(string type)
    {
        var doc = Document(); doc["locals"]![0]!["type"] = type;
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(doc.ToJsonString(), out _));
        doc["locals"]![0]!["strokes"] = new JsonArray();
        Assert.Throws<JsonException>(() => EditSettingsJson.Deserialize(doc.ToJsonString(), out _));
        doc["locals"]![0]!["strokes"] = null;
        var loaded = EditSettingsJson.Deserialize(doc.ToJsonString(), out _);
        Assert.Null(loaded.Locals![0].Strokes);
        Assert.False(JsonNode.Parse(EditSettingsJson.Serialize(loaded))!["locals"]![0]!.AsObject().ContainsKey("strokes"));
    }

    [Fact]
    public void SerializeClampsReplacementStrokesWithoutChangingSharedSnapshots()
    {
        var points = new[] { new LocalBrushPoint(-32768, 49152) };
        var strokes = new[] { new LocalBrushStroke { Radius = 2, Feather = -1, Flow = 0, Points = points } };
        var settings = new EditSettings { Locals = [new() { Type = "brush", Strokes = strokes }] };
        var snapshot = settings.Clone();
        points[0] = new(0, 0); strokes[0] = new();
        var original = settings.Locals![0].Strokes![0];
        var json = EditSettingsJson.Serialize(settings);
        Assert.True(settings.HasSameEdits(snapshot));
        Assert.Same(original, snapshot.Locals![0].Strokes![0]);
        Assert.Equal(new LocalBrushPoint(-32768, 49152), original.Points[0]);
        Assert.Equal((2d, -1d, 0d), (original.Radius, original.Feather, original.Flow));
        var loaded = EditSettingsJson.Deserialize(json, out var changed).Locals![0].Strokes![0];
        Assert.False(changed);
        Assert.Equal(new LocalBrushPoint(-16384, 32768), loaded.Points[0]);
        Assert.Equal((.25, 0d, .05), (loaded.Radius, loaded.Feather, loaded.Flow));
    }
}

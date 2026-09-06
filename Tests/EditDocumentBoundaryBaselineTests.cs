using System.Reflection;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

// Acceptance outcomes compared with the unsupported path pinned at 33e5cf5.
public sealed class EditDocumentBoundaryBaselineTests(ITestOutputHelper output)
{
    private const string V3 = """{"version":3,"exposure":0.75,"lens":{"distortion":true,"chromaticAberration":false,"vignetting":true,"baseline":"standard"}}""";
    private const string Unsupported = "System.Text.Json.JsonException|Edit settings document must declare version 3 or 4.";

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public async Task UnsupportedOutcomesAtEveryBoundary(int version)
    {
        using var fx = new CatalogVmFixture("document-boundary");
        await CreateHistoricalSchema(fx);
        using var catalog = await fx.CreateCatalogAsync();
        var path = fx.Path("synthetic.jpg");
        var id = await catalog.GetOrCreateImageAsync(path);
        var json = V3.Replace("\"version\":3", $"\"version\":{version}");
        await Store(fx, id, version, json, history: true);

        var image = await Observe(async () => EditSettingsJson.Serialize(
            (await catalog.LoadImageStatesAsync([path]))[path].Single().EditSettings));
        var history = await Observe(async () => string.Join("\n",
            (await catalog.LoadEditHistoryAsync(id)).Entries.Select(entry =>
                $"{entry.Sequence}|{entry.Label}|{EditSettingsJson.Serialize(entry.Settings)}")));
        var crop = await Observe(async () => JsonSerializer.Serialize(
            await catalog.LoadCropProjectionAsync(id)));
        var assessment = await Observe(async () => await ReadAssessmentSettings(fx, catalog, id));
        var directory = Directory.CreateDirectory(fx.Path("presets")).FullName;
        var presetPath = Path.Combine(directory, "boundary.json");
        var presetJson = "{\"version\":2,\"id\":\"boundary\",\"name\":\"Boundary\",\"settings\":" + json + "}";
        await File.WriteAllTextAsync(presetPath, presetJson);
        var presets = new PresetService(directory);
        await presets.UseDirectoryAsync(directory);
        var preset = presets.UserPresets.Count == 0 ? "skipped" :
            "loaded|" + EditSettingsJson.Serialize(Assert.Single(presets.UserPresets).Settings);

        AssertObservation(version, "image", DocumentBoundaryGoldens.Neutral, image);
        AssertObservation(version, "history", Unsupported, history);
        AssertObservation(version, "crop", Unsupported, crop);
        AssertObservation(version, "assessment", Unsupported, assessment);
        AssertObservation(version, "preset", "skipped", preset);
        // Reading either version must leave the stored documents alone.
        Assert.Equal(presetJson, await File.ReadAllTextAsync(presetPath));
        await using var connection = await Open(fx);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT edit_settings FROM images WHERE id=@id";
        command.Parameters.AddWithValue("@id", id);
        Assert.Equal(json, await command.ExecuteScalarAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task V3BaselineTolerance(bool withBaseline)
    {
        var json = withBaseline ? V3 : V3.Replace(",\"baseline\":\"standard\"", "");
        var direct = await Observe(() => Task.FromResult(
            EditSettingsJson.Serialize(EditSettingsJson.Deserialize(json, out _))));
        using var fx = new CatalogVmFixture("document-tolerance");
        await CreateHistoricalSchema(fx);
        using var catalog = await fx.CreateCatalogAsync();
        var path = fx.Path("synthetic.jpg");
        var id = await catalog.GetOrCreateImageAsync(path);
        await Store(fx, id, 3, json, history: false);
        var image = await Observe(async () => EditSettingsJson.Serialize(
            (await catalog.LoadImageStatesAsync([path]))[path].Single().EditSettings));
        Assert.DoesNotContain("baseline", direct);
        AssertObservation(3, $"direct/baseline={withBaseline}", DocumentBoundaryGoldens.Current, direct);
        AssertObservation(3, $"image/baseline={withBaseline}", DocumentBoundaryGoldens.Current, image);
    }

    private void AssertObservation(int version, string reader, string expected, string actual)
    {
        output.WriteLine($"v{version}/{reader}|{actual}");
        Assert.Equal(expected, actual);
    }

    private static async Task<string> Observe(Func<Task<string>> read)
    {
        try { return await read(); }
        catch (Exception exception) { return exception.GetType().FullName + "|" + exception.Message; }
    }

    private static async Task<string> ReadAssessmentSettings(CatalogVmFixture fx, CatalogService catalog, long id)
    {
        var snapshot = Assert.Single(await catalog.LoadAssessmentSnapshotsAsync([id]));
        var item = new XmpReconcileItem(snapshot,
            new XmpSidecarCandidate(fx.Path("synthetic.jpg.xmp"), DateTime.UnixEpoch, 1, true),
            new XmpSidecarFacts(XmpFact<int>.Missing, XmpFact<ImageFlag>.Missing,
                XmpFact<ColorLabel>.Missing, XmpFact<CropRegion>.Matched(new CropRegion())));
        await using var connection = await Open(fx);
        using var transaction = connection.BeginTransaction();
        // Invoke the actual private settings reader, isolating it from adoption writes.
        var method = typeof(CatalogService).GetMethod("ReadAdoptableCropSettingsAsync",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var settings = await (Task<EditSettings?>)method.Invoke(null,
            [connection, transaction, item, AssessmentAxes.Crop, CancellationToken.None])!;
        return settings == null ? "null" : EditSettingsJson.Serialize(settings);
    }

    private static async Task<SqliteConnection> Open(CatalogVmFixture fx)
    {
        var connection = new SqliteConnection($"Data Source={fx.Path("catalog.db")};Pooling=False");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task CreateHistoricalSchema(CatalogVmFixture fx)
    {
        // Same historical catalog setup as EditConstructionMigrationBaselineTests.
        await using var connection = await Open(fx);
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE images (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL COLLATE NOCASE UNIQUE,
                file_name TEXT NOT NULL, edit_settings TEXT,
                edit_version INTEGER NOT NULL, flag_state INTEGER DEFAULT 0,
                rating INTEGER DEFAULT 0, updated_utc TEXT);
            CREATE TABLE app_settings (key TEXT PRIMARY KEY, value TEXT);
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task Store(CatalogVmFixture fx, long id, int version, string json, bool history)
    {
        await using var connection = await Open(fx);
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE images SET edit_settings=@json, edit_version=@version WHERE id=@id;";
        if (history)
            command.CommandText += "INSERT INTO edit_history (image_id, seq, label, settings_json) VALUES (@id, 0, 'Boundary', @json);";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@json", json);
        await command.ExecuteNonQueryAsync();
    }
}

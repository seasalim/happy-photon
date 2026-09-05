using System.Text.RegularExpressions;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class EditConstructionMigrationBaselineTests
{
    [Fact]
    public async Task CatalogReaderPinsMigrationAndFallbackDocuments()
    {
        using var fx = new CatalogVmFixture("construction-migration");
        // Historical compatible catalogs can contain NULL documents; a fresh
        // catalog's NOT NULL constraint cannot represent that reader workload.
        await using (var connection = new SqliteConnection($"Data Source={fx.Path("catalog.db")};Pooling=False"))
        {
            await connection.OpenAsync();
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
        using var catalog = await fx.CreateCatalogAsync();
        const string v3 = """{"version":3,"exposure":0.75,"lens":{"distortion":true,"chromaticAberration":false,"vignetting":true,"baseline":"standard"}}""";
        var cases = new (string Name, int Version, string? Document)[]
        {
            ("v2-lens", 2, v3.Replace("\"version\":3", "\"version\":2").Replace(",\"baseline\":\"standard\"", "")),
            ("v2-no-lens", 2, """{"version":2,"exposure":0.75}"""),
            ("v3", 3, v3),
            ("v3-missing-baseline", 3, v3.Replace(",\"baseline\":\"standard\"", "")),
            ("row-0", 0, v3), ("row-1", 1, v3), ("row-4", 4, v3),
            ("marker-mismatch", 2, v3), ("missing", 3, null),
            ("malformed", 3, "{not-json")
        };
        var observations = new List<string>();
        foreach (var item in cases)
        {
            var path = fx.Path(item.Name + ".jpg");
            var id = await catalog.GetOrCreateImageAsync(path);
            await using var connection = new SqliteConnection($"Data Source={fx.Path("catalog.db")};Pooling=False");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE images SET edit_settings=@json, edit_version=@version WHERE id=@id";
            command.Parameters.AddWithValue("@json", (object?)item.Document ?? DBNull.Value);
            command.Parameters.AddWithValue("@version", item.Version);
            command.Parameters.AddWithValue("@id", id);
            await command.ExecuteNonQueryAsync();
            var loaded = (await catalog.LoadImageStatesAsync([path]))[path].Single().EditSettings;
            var json = EditSettingsJson.Serialize(loaded);
            observations.Add(item.Name + "|" + json);
            // Repeat the real read to exercise the once-per-row path as well.
            Assert.Equal(json, EditSettingsJson.Serialize(
                (await catalog.LoadImageStatesAsync([path]))[path].Single().EditSettings));
        }
        ConstructionBaselineAssert.Match("migration", observations);
    }

    [Fact]
    public void WarningSourcePinsTemplatesOrderAndPerRowSet()
    {
        var source = File.ReadAllText(Path.Combine(ConstructionBaselineAssert.Root, "Services", "CatalogService.cs"));
        var start = source.IndexOf("    private EditSettings ReadEditSettings(", StringComparison.Ordinal);
        var end = source.IndexOf("    /// <summary>Gets the cached thumbnail", start, StringComparison.Ordinal);
        // Only the acceptance predicate is deliberately outside this structural pin.
        var reader = source[start..end].Replace("\r\n", "\n");
        reader = new Regex(@"(?m)^        if \([^\n]+\)").Replace(reader, "        VERSION_POLICY", 1);
        var declaration = Regex.Match(source, @"private readonly HashSet<long> _editSettingsWarnings = new\(\);").Value;
        Assert.NotEmpty(declaration);
        ConstructionBaselineAssert.Match("warnings", [declaration, reader]);
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

internal static class BackupTestSupport
{
    internal static void CopyCatalog(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var name in new[] { "catalog.db", ".catalog-identity", AppDataRootOwnership.MarkerFileName })
            File.Copy(Path.Combine(source, name), Path.Combine(target, name));
    }

    internal static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal static Dictionary<string, string[]> Rows(string path)
    {
        using var connection = CatalogService.OpenBackupCopy(path, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        var tables = new List<string>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) tables.Add(reader.GetString(0));
        var result = new Dictionary<string, string[]>();
        foreach (var table in tables)
        {
            command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\";";
            var rows = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(JsonSerializer.Serialize(values));
            }
            result[table] = rows.Order(StringComparer.Ordinal).ToArray();
        }
        return result;
    }

    internal static void EqualRows(Dictionary<string, string[]> expected, string path)
    {
        var actual = Rows(path);
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var (table, rows) in expected) Assert.Equal(rows, actual[table]);
    }

    internal static async Task WriteConcurrentAsync(CatalogService catalog)
    {
        await Task.WhenAll(
            catalog.SaveEditSettingsWithHistoryAsync(1, new EditSettings { Exposure = 1.25 }, null,
                before: new EditSettings(), historyLabel: "Backup autosave"),
            catalog.SaveEditSettingsBatchWithHistoryAsync([
                new(2, new EditSettings { Exposure = 2.25 }),
                new(3, new EditSettings { Exposure = 2.25 })], "Backup paste"),
            catalog.MutateAssessmentsAsync([
                new(4, AssessmentAxes.Rating, Rating: 5), new(5, AssessmentAxes.Rating, Rating: 5)]));
    }

    internal static async Task AssertArchiveAsync(string zipPath, string restoreRoot)
    {
        var manifest = CatalogBackupService.VerifyArchive(zipPath);
        ZipFile.ExtractToDirectory(zipPath, restoreRoot);
        using (var copy = CatalogService.OpenBackupCopy(Path.Combine(restoreRoot, "catalog.db"), SqliteOpenMode.ReadOnly))
        {
            Assert.Equal((manifest.ImageRows, manifest.SchemaVersion), CatalogService.ReadBackupFacts(copy));
            using var command = copy.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            Assert.Equal("ok", command.ExecuteScalar());
        }
        var presets = new PresetService(Path.Combine(restoreRoot, "presets"));
        await presets.InitializeAsync();
        Assert.Equal(manifest.Entries.Keys.Count(name => name.StartsWith("presets/")), presets.UserPresets.Count);
        foreach (var preset in presets.UserPresets) Assert.Contains(preset.Settings.Exposure, new[] { 1d, 2d });
    }
}

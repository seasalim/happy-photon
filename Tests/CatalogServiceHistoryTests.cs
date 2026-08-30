using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogServiceHistoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"happy-photon-history-{Guid.NewGuid():N}");

    [Fact]
    public async Task VersionThreeCatalogMigratesAdditively()
    {
        using (var catalog = new CatalogService(_root))
            await catalog.InitializeAsync();
        await using (var connection = new SqliteConnection(
            $"Data Source={Path.Combine(_root, "catalog.db")};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE edit_history;
                ALTER TABLE images DROP COLUMN history_position;
                UPDATE app_settings SET value = '3' WHERE key = 'schema_version';
                """;
            await command.ExecuteNonQueryAsync();
        }

        using (var reopened = new CatalogService(_root))
            await reopened.InitializeAsync();

        await using var inspection = new SqliteConnection(
            $"Data Source={Path.Combine(_root, "catalog.db")};Pooling=False");
        await inspection.OpenAsync();
        using var probe = inspection.CreateCommand();
        probe.CommandText = """
            SELECT value FROM app_settings WHERE key = 'schema_version';
            SELECT COUNT(*) FROM pragma_table_info('images')
            WHERE name = 'history_position';
            """;
        using var reader = await probe.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("4", reader.GetString(0));
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
    }

    [Fact]
    public async Task AppendLoadTruncateAndClearRoundTrip()
    {
        using var catalog = new CatalogService(_root);
        await catalog.InitializeAsync();
        var id = await catalog.GetOrCreateImageAsync(Path.Combine(_root, "photo.jpg"));
        var original = new EditSettings();
        var first = new EditSettings { Exposure = 1 };
        await catalog.SaveEditSettingsWithHistoryAsync(id, first,
            new CatalogEditHistoryMutation(-1,
            [
                new(0, "Original", original),
                new(1, "Exposure +1.00", first)
            ], 1));

        var loaded = await catalog.LoadEditHistoryAsync(id);
        Assert.Equal(1, loaded.Position);
        Assert.Equal(["Original", "Exposure +1.00"],
            loaded.Entries.Select(entry => entry.Label));

        var replacement = new EditSettings { Contrast = 10 };
        await catalog.SaveEditSettingsWithHistoryAsync(id, replacement,
            new CatalogEditHistoryMutation(0,
                [new(1, "Contrast +10", replacement)], 1));
        loaded = await catalog.LoadEditHistoryAsync(id);
        Assert.Equal(["Original", "Contrast +10"],
            loaded.Entries.Select(entry => entry.Label));

        await catalog.ClearEditHistoryAsync(id);
        loaded = await catalog.LoadEditHistoryAsync(id);
        Assert.Empty(loaded.Entries);
        Assert.Equal(-1, loaded.Position);
    }

    [Fact]
    public async Task VersionStartsEmptyAndBothDeletePathsRemoveRows()
    {
        using (var catalog = new CatalogService(_root))
        {
            await catalog.InitializeAsync();
            var path = Path.Combine(_root, "photo.jpg");
            var primary = await catalog.GetOrCreateImageAsync(path);
            var edited = new EditSettings { Exposure = 1 };
            await catalog.SaveEditSettingsWithHistoryAsync(primary, edited,
                new CatalogEditHistoryMutation(-1,
                [new(0, "Original", new EditSettings()), new(1, "Edit", edited)], 1));
            var version = await catalog.CreateVersionAsync(primary);
            Assert.NotNull(version);
            Assert.Empty((await catalog.LoadEditHistoryAsync(version!.CatalogId)).Entries);

            await catalog.SaveEditSettingsWithHistoryAsync(version.CatalogId, edited,
                new CatalogEditHistoryMutation(-1,
                [new(0, "Original", edited)], 0));
            await catalog.DeleteImageAsync(version.CatalogId);
            Assert.Equal(0, await CountRowsAsync(version.CatalogId));

            await catalog.DeleteFileAsync(path);
            Assert.Equal(0, await CountRowsAsync(primary));
        }
    }

    private async Task<long> CountRowsAsync(long id)
    {
        await using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(_root, "catalog.db")};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM edit_history WHERE image_id = @id;";
        command.Parameters.AddWithValue("@id", id);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

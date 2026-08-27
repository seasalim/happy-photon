using Microsoft.Data.Sqlite;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogMigrationTests : IDisposable
{
    private readonly TemporaryDirectory _root = new();

    [Fact]
    public async Task PreLabelCatalog_MigratesBeforeValidationAndReopensAsNoOp()
    {
        await CreatePreLabelCatalogAsync();

        using (var service = new CatalogService(_root.Path))
        {
            await service.InitializeAsync();
        }
        var firstVersion = await ReadSettingAsync("schema_version");

        using (var reopened = new CatalogService(_root.Path))
        {
            await reopened.InitializeAsync();
        }

        Assert.Equal("3", firstVersion);
        Assert.Equal("3", await ReadSettingAsync("schema_version"));
        Assert.Contains("color_label", await ReadColumnsAsync());
        Assert.Contains("version", await ReadColumnsAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("4")]
    public async Task PresentInvalidVersion_FailsWithoutChangingSchema(string value)
    {
        await CreatePreLabelCatalogAsync(value);

        using var service = new CatalogService(_root.Path);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            service.InitializeAsync);

        Assert.Contains("schema", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(value, await ReadSettingAsync("schema_version"));
        Assert.DoesNotContain("color_label", await ReadColumnsAsync());
    }

    [Fact]
    public async Task FailedMigration_RollsBackSchemaAndVersionTogether()
    {
        await CreatePreLabelCatalogAsync();
        await using (var connection = await OpenAsync())
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER reject_schema_version
                BEFORE INSERT ON app_settings
                WHEN NEW.key = 'schema_version'
                BEGIN
                    SELECT RAISE(FAIL, 'reject version');
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        using var service = new CatalogService(_root.Path);
        await Assert.ThrowsAnyAsync<Exception>(service.InitializeAsync);

        Assert.Null(await ReadSettingAsync("schema_version"));
        Assert.DoesNotContain("color_label", await ReadColumnsAsync());
    }

    [Fact]
    public async Task VersionTwo_SeedsOnlyAssessedRowsWithOneCapturedTimestamp()
    {
        await using (var connection = await OpenAsync())
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE images (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    file_path TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    file_name TEXT NOT NULL,
                    edit_settings TEXT NOT NULL,
                    edit_version INTEGER NOT NULL,
                    flag_state INTEGER NOT NULL DEFAULT 0,
                    rating INTEGER NOT NULL DEFAULT 0,
                    color_label INTEGER NOT NULL DEFAULT 0,
                    updated_utc TEXT);
                CREATE TABLE app_settings (key TEXT PRIMARY KEY, value TEXT);
                INSERT INTO app_settings VALUES ('schema_version', '1');
                INSERT INTO images (file_path, file_name, edit_settings, edit_version,
                    flag_state, rating, color_label, updated_utc) VALUES
                    ('a.jpg', 'a.jpg', '{}', 2, 0, 4, 0, '1999-01-01T00:00:00Z'),
                    ('b.jpg', 'b.jpg', '{}', 2, 1, 0, 0, '2000-01-01T00:00:00Z'),
                    ('c.jpg', 'c.jpg', '{}', 2, 0, 0, 0, '2001-01-01T00:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        using (var service = new CatalogService(_root.Path))
            await service.InitializeAsync();

        await using var inspection = await OpenAsync();
        using var query = inspection.CreateCommand();
        query.CommandText =
            "SELECT revision, assessed_utc, pending_axes FROM image_assessments ORDER BY image_id;";
        var rows = new List<(long Revision, string Utc, long Pending)>();
        using var reader = await query.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)));
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal((1L, 0L), (row.Revision, row.Pending)));
        Assert.Single(rows.Select(row => row.Utc).Distinct());
        Assert.DoesNotContain(rows, row => row.Utc.StartsWith("1999", StringComparison.Ordinal));
    }

    private async Task CreatePreLabelCatalogAsync(string? version = null)
    {
        var databasePath = Path.Combine(_root.Path, "catalog.db");
        await using var connection = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE images (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL COLLATE NOCASE UNIQUE,
                file_name TEXT NOT NULL,
                edit_settings TEXT NOT NULL,
                edit_version INTEGER NOT NULL,
                flag_state INTEGER NOT NULL DEFAULT 0,
                rating INTEGER NOT NULL DEFAULT 0,
                updated_utc TEXT
            );
            CREATE TABLE app_settings (key TEXT PRIMARY KEY, value TEXT);
            """;
        await command.ExecuteNonQueryAsync();
        if (version == null) return;
        command.CommandText =
            "INSERT INTO app_settings (key, value) VALUES ('schema_version', @value);";
        command.Parameters.AddWithValue("@value", version);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> ReadSettingAsync(string key)
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_settings WHERE key = @key;";
        command.Parameters.AddWithValue("@key", key);
        return await command.ExecuteScalarAsync() as string;
    }

    private async Task<List<string>> ReadColumnsAsync()
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(images);";
        var columns = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        return columns;
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(
            $"Data Source={Path.Combine(_root.Path, "catalog.db")};Pooling=False");
        await connection.OpenAsync();
        return connection;
    }

    public void Dispose() => _root.Dispose();
}

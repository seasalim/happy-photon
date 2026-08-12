using Microsoft.Data.Sqlite;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogMigrationTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"happy-photon-migration-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task PreLabelCatalog_MigratesBeforeValidationAndReopensAsNoOp()
    {
        await CreatePreLabelCatalogAsync();

        using (var service = new CatalogService(_root))
        {
            await service.InitializeAsync();
        }
        var firstVersion = await ReadSettingAsync("schema_version");

        using (var reopened = new CatalogService(_root))
        {
            await reopened.InitializeAsync();
        }

        Assert.Equal("1", firstVersion);
        Assert.Equal("1", await ReadSettingAsync("schema_version"));
        Assert.Contains("color_label", await ReadColumnsAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("2")]
    public async Task PresentInvalidVersion_FailsWithoutChangingSchema(string value)
    {
        await CreatePreLabelCatalogAsync(value);

        using var service = new CatalogService(_root);
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

        using var service = new CatalogService(_root);
        await Assert.ThrowsAnyAsync<Exception>(service.InitializeAsync);

        Assert.Null(await ReadSettingAsync("schema_version"));
        Assert.DoesNotContain("color_label", await ReadColumnsAsync());
    }

    private async Task CreatePreLabelCatalogAsync(string? version = null)
    {
        var databasePath = Path.Combine(_root, "catalog.db");
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
            $"Data Source={Path.Combine(_root, "catalog.db")};Pooling=False");
        await connection.OpenAsync();
        return connection;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}

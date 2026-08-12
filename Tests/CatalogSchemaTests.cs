using Microsoft.Data.Sqlite;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogSchemaTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonSchema_{Guid.NewGuid():N}");
    private readonly string _movedDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonSchemaMoved_{Guid.NewGuid():N}");

    [Fact]
    public async Task NewCatalog_HasUniqueNoCasePathIndexWithoutRedundantIndexes()
    {
        using (var service = new CatalogService(_tempDirectory))
        {
            await service.InitializeAsync();
        }
        var databasePath = Path.Combine(_tempDirectory, "catalog.db");
        await using var connection = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        using var indexes = connection.CreateCommand();
        indexes.CommandText = "PRAGMA index_list('images')";
        string? uniqueAutoIndex = null;
        using (var reader = await indexes.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                Assert.NotEqual("idx_images_path", name);
                Assert.NotEqual("idx_images_path_nocase", name);
                if (reader.GetInt32(2) == 1 && reader.GetString(3) == "u")
                {
                    uniqueAutoIndex = name;
                }
            }
        }

        Assert.NotNull(uniqueAutoIndex);
        using var details = connection.CreateCommand();
        details.CommandText = $"PRAGMA index_xinfo('{uniqueAutoIndex!.Replace("'", "''")}')";
        using var detailReader = await details.ExecuteReaderAsync();
        var hasNoCaseFilePath = false;
        while (await detailReader.ReadAsync())
        {
            if (!detailReader.IsDBNull(2) &&
                detailReader.GetString(2) == "file_path" &&
                detailReader.GetString(4) == "NOCASE")
            {
                hasNoCaseFilePath = true;
            }
        }
        Assert.True(hasNoCaseFilePath);
    }

    [Fact]
    public async Task NewCatalog_HasOnlyCanonicalImageColumns()
    {
        using (var service = new CatalogService(_tempDirectory))
        {
            await service.InitializeAsync();
        }
        await using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(_tempDirectory, "catalog.db")};Pooling=False");
        await connection.OpenAsync();
        var columns = await ReadImageColumnsAsync(connection);

        Assert.Equal(
        [
            "id",
            "file_path",
            "file_name",
            "edit_settings",
            "edit_version",
            "flag_state",
            "rating",
            "color_label",
            "updated_utc"
        ],
        columns);
    }

    [Fact]
    public async Task Initialize_AcceptsRequiredColumnsWithHarmlessExtras()
    {
        using (var service = new CatalogService(_tempDirectory))
        {
            await service.InitializeAsync();
        }
        var databasePath = Path.Combine(_tempDirectory, "catalog.db");
        await using (var connection = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE images ADD COLUMN exposure REAL DEFAULT 0.0";
            await command.ExecuteNonQueryAsync();
        }

        using var reopened = new CatalogService(_tempDirectory);
        await reopened.InitializeAsync();

        await using var inspection = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        await inspection.OpenAsync();
        Assert.Contains("exposure", await ReadImageColumnsAsync(inspection));
    }

    [Fact]
    public async Task Initialize_RejectsOldSchemaAndSucceedsAfterDatabaseIsMoved()
    {
        Directory.CreateDirectory(_tempDirectory);
        var databasePath = Path.Combine(_tempDirectory, "catalog.db");
        await using (var connection = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE images (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    file_path TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    file_name TEXT NOT NULL,
                    flag_state INTEGER DEFAULT 0,
                    rating INTEGER DEFAULT 0,
                    updated_utc TEXT
                );
                CREATE TABLE app_settings (key TEXT PRIMARY KEY, value TEXT);
                """;
            await command.ExecuteNonQueryAsync();
        }

        using var service = new CatalogService(_tempDirectory);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            service.InitializeAsync);

        Assert.Contains("edit_settings", exception.Message);
        Assert.Contains("edit_version", exception.Message);
        Assert.Contains(_tempDirectory, exception.Message);
        Assert.Contains("catalog folder", exception.Message);
        Assert.Contains("Retry", exception.Message);

        var staleAsset = Path.Combine(
            _tempDirectory, "assets", "thumbs", "00", "1.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(staleAsset)!);
        await File.WriteAllTextAsync(staleAsset, "old thumbnail");
        Directory.Move(_tempDirectory, _movedDirectory);
        await service.InitializeAsync();

        Assert.True(File.Exists(Path.Combine(_movedDirectory, "catalog.db")));
        Assert.True(File.Exists(Path.Combine(
            _movedDirectory, "assets", "thumbs", "00", "1.jpg")));
        Assert.True(Directory.Exists(Path.Combine(_tempDirectory, "assets")));
        Assert.False(File.Exists(staleAsset));
        await using var replacement = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False");
        await replacement.OpenAsync();
        Assert.Equal(
        [
            "id",
            "file_path",
            "file_name",
            "edit_settings",
            "edit_version",
            "flag_state",
            "rating",
            "color_label",
            "updated_utc"
        ],
        await ReadImageColumnsAsync(replacement));
    }

    private static async Task<List<string>> ReadImageColumnsAsync(
        SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(images)";
        var columns = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        if (Directory.Exists(_movedDirectory))
        {
            Directory.Delete(_movedDirectory, recursive: true);
        }
    }
}

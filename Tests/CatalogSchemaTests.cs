using Microsoft.Data.Sqlite;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogSchemaTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonSchema_{Guid.NewGuid():N}");

    [Fact]
    public async Task Initialize_KeepsUniqueNoCaseAutoIndexAndDropsRedundantIndexes()
    {
        using (var service = new CatalogService(_tempDirectory))
        {
            await service.InitializeAsync();
        }
        var databasePath = Path.Combine(_tempDirectory, "catalog.db");
        await using (var setup = new SqliteConnection(
            $"Data Source={databasePath};Pooling=False"))
        {
            await setup.OpenAsync();
            using var create = setup.CreateCommand();
            create.CommandText = @"
                CREATE INDEX idx_images_path ON images(file_path);
                CREATE INDEX idx_images_path_nocase ON images(file_path COLLATE NOCASE);
            ";
            await create.ExecuteNonQueryAsync();
        }

        using (var service = new CatalogService(_tempDirectory))
        {
            await service.InitializeAsync();
        }

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
    public async Task NewCatalog_OmitsLegacyCacheFlagColumns()
    {
        using (var service = new CatalogService(_tempDirectory))
        {
            await service.InitializeAsync();
        }
        await using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(_tempDirectory, "catalog.db")};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(images)";
        var columns = new List<string>();
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));

        Assert.DoesNotContain("has_thumbnail", columns);
        Assert.DoesNotContain("has_preview", columns);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}

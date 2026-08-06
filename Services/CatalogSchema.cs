using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

internal static class CatalogSchema
{
    private static readonly string[] RequiredImageColumns =
    [
        "id",
        "file_path",
        "file_name",
        "edit_settings",
        "edit_version",
        "flag_state",
        "rating",
        "updated_utc"
    ];

    public static async Task InitializeAsync(SqliteConnection connection)
    {
        await CreateTablesAsync(connection);
        await ValidateImageSchemaAsync(connection);
    }

    private static async Task CreateTablesAsync(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS images (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL COLLATE NOCASE UNIQUE,
                file_name TEXT NOT NULL,
                edit_settings TEXT NOT NULL,
                edit_version INTEGER NOT NULL,
                flag_state INTEGER NOT NULL DEFAULT 0,
                rating INTEGER NOT NULL DEFAULT 0,
                updated_utc TEXT
            );
        ";
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT
            );
        ";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ValidateImageSchemaAsync(SqliteConnection connection)
    {
        var columns = await GetImageColumnsAsync(connection);
        var missing = RequiredImageColumns
            .Where(column => !columns.Contains(column))
            .ToArray();
        if (missing.Length > 0)
        {
            var databasePath = Path.GetFullPath(connection.DataSource);
            var catalogPath = Path.GetDirectoryName(databasePath)
                ?? throw new InvalidDataException("Catalog database has no parent directory.");
            throw new InvalidDataException(
                $"This catalog uses an unsupported development format " +
                $"(missing: {string.Join(", ", missing)}). Move the catalog folder " +
                $"'{catalogPath}' aside, then choose Retry to create a new catalog.");
        }
    }

    private static async Task<HashSet<string>> GetImageColumnsAsync(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(images)";

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }
}

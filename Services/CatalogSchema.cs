using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

internal static class CatalogSchema
{
    public static async Task InitializeAsync(SqliteConnection connection)
    {
        await CreateTablesAsync(connection);
        await MigrateAsync(connection);
    }

    private static async Task CreateTablesAsync(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS images (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL COLLATE NOCASE UNIQUE,
                file_name TEXT NOT NULL,

                exposure REAL DEFAULT 0.0,
                temperature INTEGER DEFAULT 0,
                brightness INTEGER DEFAULT 0,
                contrast INTEGER DEFAULT 0,
                saturation INTEGER DEFAULT 0,
                vibrance INTEGER DEFAULT 0,
                shadows INTEGER DEFAULT 0,
                highlights INTEGER DEFAULT 0,
                rotation INTEGER DEFAULT 0,
                horizon_rotation REAL DEFAULT 0.0,
                crop_data TEXT,
                curve_data TEXT,
                applied_preset_id TEXT,
                flag_state INTEGER DEFAULT 0,
                rating INTEGER DEFAULT 0,
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

    private static async Task MigrateAsync(SqliteConnection connection)
    {
        var columns = await GetImageColumnsAsync(connection);

        await AddColumnIfMissingAsync(connection, columns, "applied_preset_id", "TEXT");
        await AddColumnIfMissingAsync(connection, columns, "rotation", "INTEGER DEFAULT 0");
        await AddColumnIfMissingAsync(connection, columns, "horizon_rotation", "REAL DEFAULT 0.0");
        await AddColumnIfMissingAsync(connection, columns, "crop_data", "TEXT");
        await AddColumnIfMissingAsync(connection, columns, "is_picked", "INTEGER DEFAULT 0");

        if (!columns.Contains("flag_state"))
        {
            await ExecuteAsync(connection, "ALTER TABLE images ADD COLUMN flag_state INTEGER DEFAULT 0");
            await ExecuteAsync(connection, "UPDATE images SET flag_state = 1 WHERE is_picked = 1");
        }

        await AddColumnIfMissingAsync(connection, columns, "rating", "INTEGER DEFAULT 0");
        await ExecuteAsync(connection, "DROP INDEX IF EXISTS idx_images_path");
        await ExecuteAsync(connection, "DROP INDEX IF EXISTS idx_images_path_nocase");
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

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        HashSet<string> columns,
        string name,
        string definition)
    {
        if (columns.Contains(name))
        {
            return;
        }

        await ExecuteAsync(connection, $"ALTER TABLE images ADD COLUMN {name} {definition}");
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string commandText)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = commandText;
        await cmd.ExecuteNonQueryAsync();
    }
}

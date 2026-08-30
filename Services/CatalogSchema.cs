using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public sealed class CatalogSchemaMismatchException(
    string message,
    IReadOnlyList<string> missingColumns) : Exception(message)
{
    public IReadOnlyList<string> MissingColumns { get; } = missingColumns;
}

internal static class CatalogSchema
{
    private static readonly string[] RequiredImageColumns =
    [
        "id",
        "file_path",
        "version",
        "version_label",
        "file_name",
        "edit_settings",
        "edit_version",
        "flag_state",
        "rating",
        "color_label",
        "history_position",
        "updated_utc"
    ];

    public static async Task InitializeAsync(SqliteConnection connection)
    {
        await CreateTablesAsync(connection);
        await CatalogMigrations.RunAsync(connection);
        await ValidateImageSchemaAsync(connection);
        await ValidateAssessmentSchemaAsync(connection);
    }

    private static async Task ValidateAssessmentSchemaAsync(
        SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(image_assessments);";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(1));

        string[] required = ["image_id", "revision", "assessed_utc", "pending_axes"];
        if (required.Any(column => !columns.Contains(column)))
        {
            throw new InvalidDataException(
                "This catalog has an unsupported image assessment schema.");
        }
    }

    private static async Task CreateTablesAsync(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS images (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL COLLATE NOCASE,
                version INTEGER NOT NULL DEFAULT 1 CHECK (version BETWEEN 1 AND 8),
                version_label TEXT,
                file_name TEXT NOT NULL,
                edit_settings TEXT NOT NULL,
                edit_version INTEGER NOT NULL,
                flag_state INTEGER NOT NULL DEFAULT 0,
                rating INTEGER NOT NULL DEFAULT 0,
                color_label INTEGER NOT NULL DEFAULT 0,
                history_position INTEGER NOT NULL DEFAULT -1,
                updated_utc TEXT,
                UNIQUE (file_path, version)
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
            throw new CatalogSchemaMismatchException(
                $"This catalog uses an unsupported development format " +
                $"(missing: {string.Join(", ", missing)}). Set the catalog and cache " +
                $"at '{catalogPath}' aside together, then retry.",
                missing);
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

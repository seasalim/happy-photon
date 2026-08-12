using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

internal static class CatalogMigrations
{
    internal const int CurrentVersion = 1;
    private const string SchemaVersionKey = "schema_version";

    public static async Task RunAsync(SqliteConnection connection)
    {
        var version = await ReadVersionAsync(connection);
        if (version > CurrentVersion)
        {
            throw new InvalidDataException(
                $"Catalog schema version {version} is newer than this build supports " +
                $"({CurrentVersion}). Upgrade Happy Photon to open this catalog.");
        }

        for (var next = version + 1; next <= CurrentVersion; next++)
        {
            using var transaction = connection.BeginTransaction();
            await ApplyAsync(connection, transaction, next);
            await WriteVersionAsync(connection, transaction, next);
            await transaction.CommitAsync();
        }
    }

    private static async Task<int> ReadVersionAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT value FROM app_settings WHERE key = @key;";
        command.Parameters.AddWithValue("@key", SchemaVersionKey);
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return 0;

        var text = reader.IsDBNull(0) ? null : reader.GetString(0);
        if (string.IsNullOrWhiteSpace(text) ||
            !int.TryParse(text, out var version) ||
            version < 0)
        {
            throw new InvalidDataException(
                $"Catalog schema_version '{text ?? "<null>"}' is malformed.");
        }

        return version;
    }

    private static Task ApplyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version) => version switch
        {
            1 => AddColorLabelAsync(connection, transaction),
            _ => throw new InvalidDataException(
                $"Catalog migration {version} is not available.")
        };

    private static async Task AddColorLabelAsync(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var probe = connection.CreateCommand();
        probe.Transaction = transaction;
        probe.CommandText = "PRAGMA table_info(images);";
        using (var reader = await probe.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (string.Equals(
                    reader.GetString(1),
                    "color_label",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "ALTER TABLE images ADD COLUMN color_label INTEGER NOT NULL DEFAULT 0;";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WriteVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO app_settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("@key", SchemaVersionKey);
        command.Parameters.AddWithValue("@value", version.ToString());
        await command.ExecuteNonQueryAsync();
    }
}

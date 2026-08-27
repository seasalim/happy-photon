using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

internal static class CatalogMigrations
{
    internal const int CurrentVersion = 3;
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

        if (version < 3 && !await HasImageColumnAsync(connection, "version", null))
        {
            var backupPath = connection.DataSource + ".pre-versions-backup";
            File.Copy(connection.DataSource, backupPath, overwrite: true);
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
            2 => AddImageAssessmentsAsync(connection, transaction),
            3 => AddVersionsAsync(connection, transaction),
            _ => throw new InvalidDataException(
                $"Catalog migration {version} is not available.")
        };

    private static async Task<bool> HasImageColumnAsync(
        SqliteConnection connection,
        string column,
        SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA table_info(images);";
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), column,
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task AddVersionsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        if (await HasImageColumnAsync(connection, "version", transaction)) return;
        foreach (var required in new[]
                 {
                     "edit_settings", "edit_version", "flag_state",
                     "rating", "color_label", "updated_utc"
                 })
        {
            if (!await HasImageColumnAsync(connection, required, transaction))
                return;
        }

        long sequence = 0;
        using (var sequenceCommand = connection.CreateCommand())
        {
            sequenceCommand.Transaction = transaction;
            sequenceCommand.CommandText =
                "SELECT seq FROM sqlite_sequence WHERE name = 'images';";
            sequence = (long?)await sequenceCommand.ExecuteScalarAsync() ?? 0;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TEMP TABLE assessment_backup AS
            SELECT image_id, revision, assessed_utc, pending_axes
            FROM image_assessments;
            DROP TABLE image_assessments;
            ALTER TABLE images RENAME TO images_v2;
            CREATE TABLE images (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL COLLATE NOCASE,
                version INTEGER NOT NULL DEFAULT 1 CHECK (version BETWEEN 1 AND 8),
                version_label TEXT,
                file_name TEXT NOT NULL,
                edit_settings TEXT,
                edit_version INTEGER NOT NULL,
                flag_state INTEGER NOT NULL DEFAULT 0,
                rating INTEGER NOT NULL DEFAULT 0,
                color_label INTEGER NOT NULL DEFAULT 0,
                updated_utc TEXT,
                UNIQUE (file_path, version));
            INSERT INTO images (
                id, file_path, version, file_name, edit_settings, edit_version,
                flag_state, rating, color_label, updated_utc)
            SELECT id, file_path, 1, file_name, edit_settings, edit_version,
                   COALESCE(flag_state, 0), COALESCE(rating, 0),
                   COALESCE(color_label, 0), updated_utc
            FROM images_v2;
            DROP TABLE images_v2;
            CREATE TABLE image_assessments (
                image_id INTEGER PRIMARY KEY,
                revision INTEGER NOT NULL,
                assessed_utc TEXT NOT NULL,
                pending_axes INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (image_id) REFERENCES images(id) ON DELETE CASCADE);
            INSERT INTO image_assessments
            SELECT image_id, revision, assessed_utc, pending_axes
            FROM assessment_backup;
            DROP TABLE assessment_backup;
            INSERT INTO sqlite_sequence(name, seq)
            SELECT 'images', @sequence
            WHERE NOT EXISTS (
                SELECT 1 FROM sqlite_sequence WHERE name = 'images');
            UPDATE sqlite_sequence
            SET seq = MAX(seq, @sequence)
            WHERE name = 'images';
            """;
        command.Parameters.AddWithValue("@sequence", sequence);
        await command.ExecuteNonQueryAsync();

        command.Parameters.Clear();
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            throw new InvalidDataException(
                "Catalog version migration failed its foreign-key check.");
    }

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

    private static async Task AddImageAssessmentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE image_assessments (
                image_id INTEGER PRIMARY KEY,
                revision INTEGER NOT NULL,
                assessed_utc TEXT NOT NULL,
                pending_axes INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (image_id) REFERENCES images(id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync();

        var capturedUtc = DateTime.UtcNow.ToString("O");
        command.CommandText = """
            INSERT INTO image_assessments (
                image_id, revision, assessed_utc, pending_axes)
            SELECT id, 1, @assessedUtc, 0
            FROM images
            WHERE rating <> 0 OR flag_state <> 0 OR color_label <> 0;
            """;
        command.Parameters.AddWithValue("@assessedUtc", capturedUtc);
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

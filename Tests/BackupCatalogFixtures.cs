using System.Globalization;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

[assembly: AssemblyFixture(typeof(HappyPhoton.Tests.BackupCatalogFixtures))]

namespace HappyPhoton.Tests;

/// <summary>
/// Session-owned, lazily built controls for backup gates. Treat returned catalogs as
/// immutable; mutation/termination gates should copy them into their own directory.
/// No source photographs are created or read. Current schema and JSON use production code;
/// prepared SQL only accelerates test data insertion, outside measured operations.
/// </summary>
public sealed class BackupCatalogFixtures : IDisposable
{
    public const int Seed = 278_001;
    private readonly Lazy<TemporaryDirectory> _directory = new(() => new());
    private readonly Lazy<Task<BackupCatalogFixture>> _archive;
    private readonly Lazy<Task<BackupCatalogFixture>> _everyday;
    private readonly Lazy<Task<BackupCatalogFixture>> _schema3Archive;

    public BackupCatalogFixtures()
    {
        _archive = new(() => BuildAsync("archive", 100_000, 20_000, 10));
        _everyday = new(() => BuildAsync("everyday", 2_000, 160, 6));
        _schema3Archive = new(BuildSchema3ArchiveAsync);
    }

    public Task<BackupCatalogFixture> Archive => _archive.Value;
    public Task<BackupCatalogFixture> Everyday => _everyday.Value;

    public Task<BackupCatalogFixture> Schema3Archive => _schema3Archive.Value;

    private async Task<BackupCatalogFixture> BuildSchema3ArchiveAsync()
    {
        var archive = await Archive;
        var root = Path.Combine(_directory.Value.Path, "schema3-archive");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "catalog.db");
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = archive.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        source.Open();
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Pooling = false
        }.ToString());
        destination.Open();
        using var schema = destination.CreateCommand();
        // Exact migration-3 shape; do not run current schema initialization here.
        schema.CommandText = """
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
            CREATE TABLE image_assessments (
                image_id INTEGER PRIMARY KEY,
                revision INTEGER NOT NULL,
                assessed_utc TEXT NOT NULL,
                pending_axes INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (image_id) REFERENCES images(id) ON DELETE CASCADE);
            CREATE TABLE app_settings (key TEXT PRIMARY KEY, value TEXT);
            INSERT INTO app_settings VALUES ('schema_version', '3');
            """;
        schema.ExecuteNonQuery();
        using var transaction = destination.BeginTransaction();

        // Schema 3 has no edit history, so one copy of the archive rows is ~94 MiB. The owner
        // ruled (2026-09-27) to scale rows into G1's 160-260 MiB envelope: two full passes plus
        // a quarter, each with offset ids and its own virtual path prefix.
        var passLimits = new[] { archive.ImageRows, archive.ImageRows, archive.ImageRows / 4 };

        foreach (var (table, columns) in new[]
        {
            ("images", "id, file_path, version, version_label, file_name, edit_settings, " +
                "edit_version, flag_state, rating, color_label, updated_utc"),
            ("image_assessments", "image_id, revision, assessed_utc, pending_axes")
        })
        {
            for (var pass = 0; pass < passLimits.Length; pass++)
            {
                using var read = source.CreateCommand();
                read.CommandText = $"SELECT {columns} FROM {table} WHERE {columns.Split(',')[0]} <= {passLimits[pass]} ORDER BY 1;";
                using var reader = read.ExecuteReader();
                var parameters = Enumerable.Range(0, reader.FieldCount).Select(i => $"p{i}").ToArray();
                using var insert = Command(destination, transaction,
                    $"INSERT INTO {table} ({columns}) VALUES ({string.Join(", ", parameters.Select(p => "@" + p))});",
                    parameters);
                var values = new object[reader.FieldCount];

                while (reader.Read())
                {
                    reader.GetValues(values);
                    values[0] = Convert.ToInt64(values[0]) + (long)pass * archive.ImageRows;
                    if (table == "images" && pass > 0) values[1] = $"/pass{pass}{values[1]}";

                    Set(insert, values);
                    insert.ExecuteNonQuery();
                }
            }
        }

        transaction.Commit();

        var rows = passLimits.Sum();
        var editedRows = passLimits.Sum(limit => Math.Min(limit, archive.EditedRows));

        return new BackupCatalogFixture(root, rows, editedRows, 0, new FileInfo(databasePath).Length);
    }

    private async Task<BackupCatalogFixture> BuildAsync(
        string name, int rows, int editedRows, int historyLength)
    {
        var root = Path.Combine(_directory.Value.Path, name);
        using (var catalog = new CatalogService(root))
            await catalog.InitializeAsync();

        var databasePath = Path.Combine(root, "catalog.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString()))
        {
            connection.Open();
            using (var transaction = connection.BeginTransaction())
            {
                using var image = Command(connection, transaction, """
                    INSERT INTO images(id, file_path, version, file_name, edit_settings,
                        edit_version, flag_state, rating, color_label, history_position, updated_utc)
                    VALUES (@id, @path, 1, @name, @json, @version, @flag, @rating, @color, @position, @utc);
                    """, "id", "path", "name", "json", "version", "flag", "rating", "color", "position", "utc");
                using var assessment = Command(connection, transaction, """
                    INSERT INTO image_assessments(image_id, revision, assessed_utc, pending_axes)
                    VALUES (@id, 1, @utc, 0);
                    """, "id", "utc");
                using var history = Command(connection, transaction, """
                    INSERT INTO edit_history(image_id, seq, label, settings_json)
                    VALUES (@id, @seq, @label, @json);
                    """, "id", "seq", "label", "json");
                var random = new Random(Seed);
                var original = EditSettingsJson.Serialize(new EditSettings());
                for (var id = 1; id <= rows; id++)
                {
                    var snapshots = id <= editedRows
                        ? BackupFixtureEdits.CreateHistory(random, historyLength +
                            (name == "everyday" ? id % 2 : 0))
                        : [];
                    var utc = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        .AddSeconds(id).ToString("O", CultureInfo.InvariantCulture);
                    var fileName = $"IMG_{id:D6}.cr3";
                    // Fixed virtual paths keep DB size independent of temp/user path length.
                    Set(image, id, $"/backup-fixture/{name}/{id / 1000:D3}/{fileName}",
                        fileName, snapshots.Length == 0 ? original : snapshots[^1].Json,
                        EditSettings.CurrentVersion, id % 3, id % 6, id % 6,
                        snapshots.Length - 1, utc);
                    image.ExecuteNonQuery();
                    Set(assessment, id, utc);
                    assessment.ExecuteNonQuery();
                    for (var seq = 0; seq < snapshots.Length; seq++)
                    {
                        Set(history, id, seq, snapshots[seq].Label, snapshots[seq].Json);
                        history.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }
            using var checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            checkpoint.ExecuteNonQuery();
        }
        return new BackupCatalogFixture(root, rows, editedRows,
            editedRows * historyLength + (name == "everyday" ? editedRows / 2 : 0),
            new FileInfo(databasePath).Length);
    }

    private static SqliteCommand Command(SqliteConnection connection,
        SqliteTransaction transaction, string sql, params string[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.Add(new SqliteParameter($"@{parameter}", DBNull.Value));
        command.Prepare();
        return command;
    }

    private static void Set(SqliteCommand command, params object[] values)
    {
        for (var i = 0; i < values.Length; i++) command.Parameters[i].Value = values[i];
    }

    public void Dispose()
    {
        if (_directory.IsValueCreated) _directory.Value.Dispose();
    }
}

public sealed record BackupCatalogFixture(
    string Root, int ImageRows, int EditedRows, int HistoryRows, long SizeBytes)
{
    public string DatabasePath => Path.Combine(Root, "catalog.db");
    public double SizeMiB => SizeBytes / (1024d * 1024);
}

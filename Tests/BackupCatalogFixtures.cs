using System.Globalization;
using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

[assembly: AssemblyFixture(typeof(HappyPhoton.Tests.BackupCatalogFixtures))]

namespace HappyPhoton.Tests;

/// <summary>
/// Session-owned, lazily built controls for BACKUP-WP1. Treat returned catalogs as
/// immutable; mutation/termination gates should copy them into their own directory.
/// No source photographs are created or read. Schema and JSON use production code;
/// prepared SQL only accelerates test data insertion, outside measured operations.
/// </summary>
public sealed class BackupCatalogFixtures : IDisposable
{
    public const int Seed = 278_001;
    private readonly Lazy<TemporaryDirectory> _directory = new(() => new());
    private readonly Lazy<Task<BackupCatalogFixture>> _archive;
    private readonly Lazy<Task<BackupCatalogFixture>> _everyday;

    public BackupCatalogFixtures()
    {
        _archive = new(() => BuildAsync("archive", 100_000, 20_000, 10));
        _everyday = new(() => BuildAsync("everyday", 2_000, 160, 6));
    }

    public Task<BackupCatalogFixture> Archive => _archive.Value;
    public Task<BackupCatalogFixture> Everyday => _everyday.Value;

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

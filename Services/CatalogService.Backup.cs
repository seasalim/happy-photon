using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public partial class CatalogService
{
    internal Guid? OpenCatalogIdentity => _initialized ? _identity?.CatalogId : null;
    internal Action<double>? BackupGateMeasured { get; set; }

    internal async Task<(long Rows, long Schema)> SnapshotAsync(string path, Action<string>? step)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync().ConfigureAwait(false);
        var timer = Stopwatch.StartNew();
        try
        {
            var facts = ReadBackupFacts(_connection!);
            step?.Invoke("before:snapshot");
            using var copy = OpenBackupCopy(path, SqliteOpenMode.ReadWriteCreate);
            _connection!.BackupDatabase(copy);
            step?.Invoke("after:snapshot");
            return facts;
        }
        finally
        {
            _connectionGate.Release();
            BackupGateMeasured?.Invoke(timer.Elapsed.TotalMilliseconds);
        }
    }

    internal static SqliteConnection OpenBackupCopy(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    internal static (long Rows, long Schema) ReadBackupFacts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM images;";
        var rows = Convert.ToInt64(command.ExecuteScalar());
        command.CommandText = "SELECT value FROM app_settings WHERE key = 'schema_version';";
        return (rows, Convert.ToInt64(command.ExecuteScalar()));
    }
}

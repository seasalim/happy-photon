using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

[Collection(BackupBaselineCollection.Name)]
public sealed class BackupCrashLockDiagnosticsTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupFailure_ReportsSelfOwnerAndPreservesException(bool nested)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows file-sharing diagnostic.");

        using var directory = new TemporaryDirectory();
        var folder = nested ? Path.Combine(directory.Path, "Backups") : directory.Path;
        Directory.CreateDirectory(folder);
        var database = Path.Combine(folder, nested ? "held.partial.db" : "catalog.db");
        File.WriteAllText(database, "diagnostic control");
        directory.CleanupFailure = exception =>
            BackupCrashLockDiagnostics.Cleanup(exception, directory.Path, 0, output);

        using (var held = new FileStream(database, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => directory.Dispose());
        }

        Assert.Contains("trigger=cleanup", output.Output);
        Assert.Contains("file=" + database, output.Output);
        Assert.Contains("ownership=test-process", output.Output);
        Assert.Contains("application_type=", output.Output);
        Assert.Contains("lock_duration=no-success-observed", output.Output);
        Assert.Contains("target_ms=2000", output.Output);
        Assert.Contains("sqlite_pool_state=observed", output.Output);
        Assert.Contains("connections=0 idle=0 checked_out=0", output.Output);
    }

    [Fact]
    public void PoolSnapshot_DistinguishesIdleFromCheckedOutAndCleared()
    {
        using var directory = new TemporaryDirectory();
        var database = Path.Combine(directory.Path, "catalog.db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = database, Pooling = true }.ToString());

        try
        {
            connection.Open();
            Assert.Contains("connections=1 idle=0 checked_out=1", BackupCrashSqliteDiagnostics.Snapshot(database));
            connection.Close();
            Assert.Contains("connections=1 idle=1 checked_out=0", BackupCrashSqliteDiagnostics.Snapshot(database));
            SqliteConnection.ClearPool(connection);
            Assert.Contains("connections=0 idle=0 checked_out=0", BackupCrashSqliteDiagnostics.Snapshot(database));
        }
        finally
        {
            SqliteConnection.ClearPool(connection);
        }
    }

    [Fact]
    public void ReleasedLock_ReportsImmediateSuccessWithoutInventingAnOwner()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows file-sharing diagnostic.");

        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "released.db");
        File.WriteAllText(path, "diagnostic control");
        IOException violation;

        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            violation = Assert.Throws<IOException>(() => File.Delete(path));
        }

        var evidence = BackupCrashLockDiagnostics.Capture(violation, directory.Path, 0);
        Assert.Contains("exclusive_probe=opened target_ms=0", evidence);
        Assert.Contains("ownership=unproven", evidence);
    }
}

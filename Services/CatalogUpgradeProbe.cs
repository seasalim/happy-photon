using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

internal static class CatalogUpgradeProbe
{
    internal static async Task<bool> NeedsUpgradeAsync(string database) =>
        await ReadVersionAsync(database) is { } version && version < CatalogMigrations.CurrentVersion;

    internal static async Task<int?> ReadVersionAsync(string database)
    {
        if (!File.Exists(database)) return null;

        try
        {
            return await ProbeAsync(database, SqliteOpenMode.ReadOnly);
        }
        catch (SqliteException) when (File.Exists(database + "-journal"))
        {
            var temporary = Path.Combine(Path.GetTempPath(), $"hp-upgrade-probe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporary);

            try
            {
                var copy = Path.Combine(temporary, "catalog.db");
                File.Copy(database, copy);
                File.Copy(database + "-journal", copy + "-journal");

                return await ProbeAsync(copy, SqliteOpenMode.ReadWrite);
            }
            finally
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static async Task<int?> ProbeAsync(string path, SqliteOpenMode mode)
    {
        using var connection = CatalogService.OpenBackupCopy(path, mode);
        try
        {
            return await CatalogMigrations.ReadVersionAsync(connection);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='app_settings';";
            if (Convert.ToInt64(await command.ExecuteScalarAsync()) != 0) throw;

            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table';";

            return Convert.ToInt64(await command.ExecuteScalarAsync()) == 0 ? null : 0;
        }
    }
}

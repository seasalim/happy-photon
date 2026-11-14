using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

internal static class SqliteColumns
{
    /// <summary>Reads a table's column names (case-insensitive) with <c>PRAGMA table_info</c>.</summary>
    public static async Task<HashSet<string>> ReadAsync(
        SqliteConnection connection,
        string table,
        SqliteTransaction? transaction = null,
        CancellationToken cancellationToken = default)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }
}

using System.Globalization;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public partial class CatalogService
{
    // A stored rating outside 0–5 reads as the nearest valid star count.
    private static int ReadRating(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? 0 : (int)Math.Clamp(reader.GetInt64(ordinal), 0, 5);

    private static DateTime ReadUtc(SqliteDataReader reader, int ordinal) =>
        DateTime.Parse(reader.GetString(ordinal), null, DateTimeStyles.RoundtripKind);

    private static DateTime? ReadUtcOrNull(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadUtc(reader, ordinal);

    // Clears the command's parameters for each row; callers that reuse the command set them again.
    private static async Task InsertHistoryRowsAsync(
        SqliteCommand command, long catalogId, IEnumerable<CatalogHistoryRow> rows)
    {
        foreach (var row in rows)
        {
            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO edit_history (image_id, seq, label, settings_json)
                VALUES (@id, @seq, @label, @settings);
                """;
            command.Parameters.AddWithValue("@id", catalogId);
            command.Parameters.AddWithValue("@seq", row.Sequence);
            command.Parameters.AddWithValue("@label", row.Label);
            command.Parameters.AddWithValue("@settings", row.SettingsJson);
            await command.ExecuteNonQueryAsync();
        }
    }
}

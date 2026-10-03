using HappyPhoton.Models;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public partial class CatalogService
{
    public async Task<IReadOnlyList<CatalogEditBatchTarget>> SaveEditSettingsBatchWithHistoryAsync(
        IReadOnlyList<CatalogEditSettingsUpdate> updates, string operation)
    {
        EnsureInitialized();
        if (updates.Count == 0) return [];

        var result = new List<CatalogEditBatchTarget>(updates.Count);
        var serialized = updates.Select(SerializeUpdate).ToArray();

        await InHistoryTransactionAsync(async transaction =>
        {
            for (var index = 0; index < updates.Count; index++)
            {
                var update = updates[index];
                var before = await ReadBatchStateAsync(transaction, update.CatalogId, forAppend: true)
                    ?? throw new InvalidOperationException($"Catalog image {update.CatalogId} was not found.");
                var current = before.Rows.FirstOrDefault(row => row.Sequence == before.Position);
                var mutation = CatalogEditHistory.PrepareAppend(before.Position,
                    current == null ? null : EditSettingsJson.Deserialize(current.SettingsJson, out _),
                    update.Previous ?? update.Settings, update.Settings, operation);
                var appended = mutation?.Appended.Select(entry => new CatalogHistoryRow(
                    entry.Sequence, entry.Label, entry.Sequence == mutation.Position
                        ? serialized[index].SettingsJson : EditSettingsJson.Serialize(entry.Settings))).ToArray() ?? [];
                result.Add(new(update.CatalogId, before.Json, before.Version,
                    serialized[index].SettingsJson,
                    before.Position, before.Rows.LastOrDefault(),
                    mutation == null ? [] : before.Rows.Where(row => row.Sequence > mutation.TruncateAfter).ToArray(),
                    appended));
                await WriteSettingsAsync(transaction, serialized[index]);

                if (mutation != null)
                {
                    await WriteHistoryRowsAsync(transaction, update.CatalogId,
                        mutation.TruncateAfter, appended, mutation.Position);
                }
            }
        });

        return result;
    }

    public async Task<CatalogEditBatchUndoResult> UndoEditSettingsBatchAsync(
        IReadOnlyList<CatalogEditBatchTarget> batch)
    {
        EnsureInitialized();
        var restored = new List<long>();
        var skipped = new List<long>();

        await InHistoryTransactionAsync(async transaction =>
        {
            foreach (var target in batch)
            {
                var current = await ReadBatchStateAsync(transaction, target.CatalogId);
                var last = target.Appended.LastOrDefault();

                if (current == null || current.Json != target.WrittenJson ||
                    current.Position != (last?.Sequence ?? target.PreviousPosition) ||
                    current.Rows.LastOrDefault() != (last ?? target.PreviousNewest) ||
                    target.Appended.Any(row => !current.Rows.Contains(row)))
                {
                    skipped.Add(target.CatalogId);
                    continue;
                }

                using var command = _connection!.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE images SET edit_settings = @settings, edit_version = @version,
                        history_position = @position, updated_utc = @updated WHERE id = @id;
                    """;
                command.Parameters.AddWithValue("@id", target.CatalogId);
                command.Parameters.AddWithValue("@settings", (object?)target.PreviousJson ?? DBNull.Value);
                command.Parameters.AddWithValue("@version", target.PreviousVersion);
                command.Parameters.AddWithValue("@position", target.PreviousPosition);
                command.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();

                if (last != null)
                {
                    command.CommandText = "DELETE FROM edit_history WHERE image_id = @id AND seq > @position;";
                    await command.ExecuteNonQueryAsync();

                    foreach (var row in target.Deleted)
                    {
                        command.Parameters.Clear();
                        command.CommandText = """
                            INSERT INTO edit_history (image_id, seq, label, settings_json)
                            VALUES (@id, @seq, @label, @settings);
                            """;
                        command.Parameters.AddWithValue("@id", target.CatalogId);
                        command.Parameters.AddWithValue("@seq", row.Sequence);
                        command.Parameters.AddWithValue("@label", row.Label);
                        command.Parameters.AddWithValue("@settings", row.SettingsJson);
                        await command.ExecuteNonQueryAsync();
                    }
                }

                restored.Add(target.CatalogId);
            }
        });

        return new(restored, skipped);
    }

    private sealed record BatchState(string? Json, int Version, int Position, List<CatalogHistoryRow> Rows);

    private async Task<BatchState?> ReadBatchStateAsync(SqliteTransaction transaction, long id,
        bool forAppend = false)
    {
        using var command = _connection!.CreateCommand();
        command.Transaction = transaction;
        // Keep document/redo bytes verbatim, but append only needs the current step's decoded settings.
        command.CommandText = """
            SELECT edit_settings, edit_version, history_position FROM images WHERE id = @id;
            SELECT seq, label, settings_json FROM edit_history WHERE image_id = @id
                AND seq >= CASE WHEN @append THEN
                    (SELECT history_position FROM images WHERE id = @id) ELSE 0 END ORDER BY seq;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@append", forAppend);
        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var state = new BatchState(reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetInt32(1), reader.GetInt32(2), []);
        await reader.NextResultAsync();

        while (await reader.ReadAsync())
        {
            state.Rows.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }

        return state;
    }
}

using HappyPhoton.Models;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public partial class CatalogService
{
    internal Func<long, Task>? EditHistoryLoadGateAsync { get; set; }

    internal Func<Task>? EditHistoryWriteGateAsync { get; set; }

    public async Task<CatalogEditHistoryState> LoadEditHistoryAsync(long catalogId)
    {
        EnsureInitialized();
        if (EditHistoryLoadGateAsync is { } loadGate)
        {
            await loadGate(catalogId);
        }
        await _connectionGate.WaitAsync();
        try
        {
            return await ReadHistoryAsync(_connection!, null, catalogId);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task SaveEditSettingsWithHistoryAsync(
        long catalogId, EditSettings settings, CatalogEditHistoryMutation? mutation,
        int? position = null)
    {
        EnsureInitialized();
        var update = SerializeUpdate(new(catalogId, settings));
        await InHistoryTransactionAsync(async transaction =>
        {
            await WriteSettingsAsync(transaction, update);
            if (mutation != null)
                await WriteHistoryMutationAsync(transaction, catalogId, mutation);
            else if (position.HasValue)
                await WritePositionAsync(transaction, catalogId, position.Value);
        });
    }

    public async Task SaveEditSettingsBatchWithHistoryAsync(
        IReadOnlyList<CatalogEditSettingsUpdate> updates, string operation)
    {
        EnsureInitialized();
        if (updates.Count == 0) return;
        var serialized = updates.Select(SerializeUpdate).ToArray();
        await InHistoryTransactionAsync(async transaction =>
        {
            for (var index = 0; index < updates.Count; index++)
            {
                var update = updates[index];
                var state = await ReadHistoryAsync(
                    _connection!, transaction, update.CatalogId);
                var mutation = CatalogEditHistory.PrepareAppend(
                    state,
                    update.Previous ?? update.Settings,
                    update.Settings,
                    operation);
                await WriteSettingsAsync(transaction, serialized[index]);
                if (mutation != null)
                    await WriteHistoryMutationAsync(
                        transaction, update.CatalogId, mutation);
            }
        });
    }

    public async Task ClearEditHistoryAsync(long catalogId)
    {
        EnsureInitialized();
        await InHistoryTransactionAsync(async transaction =>
        {
            using var command = _connection!.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM edit_history WHERE image_id = @id;";
            command.Parameters.AddWithValue("@id", catalogId);
            await command.ExecuteNonQueryAsync();
            await WritePositionAsync(transaction, catalogId, -1);
        });
    }

    private async Task InHistoryTransactionAsync(Func<SqliteTransaction, Task> action)
    {
        if (EditHistoryWriteGateAsync is { } writeGate)
        {
            await writeGate();
        }
        await _connectionGate.WaitAsync();
        try
        {
            using var transaction = _connection!.BeginTransaction();
            await action(transaction);
            await transaction.CommitAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task WriteSettingsAsync(
        SqliteTransaction transaction, SerializedEditUpdate update)
    {
        using var command = CreateSaveEditSettingsCommand();
        command.Transaction = transaction;
        ApplyUpdateParameters(command, update);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new InvalidOperationException(
                $"Catalog image {update.CatalogId} was not updated.");
    }
    private static async Task<CatalogEditHistoryState> ReadHistoryAsync(
        SqliteConnection connection, SqliteTransaction? transaction, long catalogId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT history_position FROM images WHERE id = @id;
            SELECT seq, label, settings_json FROM edit_history
            WHERE image_id = @id ORDER BY seq;
            """;
        command.Parameters.AddWithValue("@id", catalogId);
        using var reader = await command.ExecuteReaderAsync();
        var position = await reader.ReadAsync() ? reader.GetInt32(0) : -1;
        await reader.NextResultAsync();
        var entries = new List<CatalogEditHistoryEntry>();
        while (await reader.ReadAsync())
            entries.Add(new(reader.GetInt32(0), reader.GetString(1),
                EditSettingsJson.Deserialize(reader.GetString(2), out _)));
        return new(entries, position);
    }

    private async Task WriteHistoryMutationAsync(
        SqliteTransaction transaction, long catalogId,
        CatalogEditHistoryMutation mutation)
    {
        using var command = _connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM edit_history WHERE image_id = @id AND seq > @position;";
        command.Parameters.AddWithValue("@id", catalogId);
        command.Parameters.AddWithValue("@position", mutation.TruncateAfter);
        await command.ExecuteNonQueryAsync();
        foreach (var entry in mutation.Appended)
        {
            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO edit_history (image_id, seq, label, settings_json)
                VALUES (@id, @seq, @label, @settings);
                """;
            command.Parameters.AddWithValue("@id", catalogId);
            command.Parameters.AddWithValue("@seq", entry.Sequence);
            command.Parameters.AddWithValue("@label", entry.Label);
            command.Parameters.AddWithValue(
                "@settings", EditSettingsJson.Serialize(entry.Settings));
            await command.ExecuteNonQueryAsync();
        }
        await WritePositionAsync(transaction, catalogId, mutation.Position);
    }

    private async Task WritePositionAsync(
        SqliteTransaction transaction, long catalogId, int position)
    {
        using var command = _connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE images SET history_position = @position WHERE id = @id;";
        command.Parameters.AddWithValue("@position", position);
        command.Parameters.AddWithValue("@id", catalogId);
        await command.ExecuteNonQueryAsync();
    }
}

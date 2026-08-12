using HappyPhoton.Models;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public partial class CatalogService
{
    private const string SaveEditSettingsSql = @"
        UPDATE images SET
            edit_settings = @editSettings,
            edit_version = @editVersion,
            updated_utc = @updated
        WHERE id = @id;
    ";

    /// <summary>Saves edit settings for an image.</summary>
    public async Task SaveEditSettingsAsync(long catalogId, EditSettings settings)
    {
        EnsureInitialized();
        var update = SerializeUpdate(new CatalogEditSettingsUpdate(catalogId, settings));

        await _connectionGate.WaitAsync();
        try
        {
            using var cmd = CreateSaveEditSettingsCommand();
            ApplyUpdateParameters(cmd, update);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <summary>Saves edit settings atomically for a group of images.</summary>
    public async Task SaveEditSettingsBatchAsync(
        IReadOnlyList<CatalogEditSettingsUpdate> updates)
    {
        EnsureInitialized();
        if (updates.Count == 0) return;
        var serializedUpdates = updates.Select(SerializeUpdate).ToArray();

        await _connectionGate.WaitAsync();
        try
        {
            using var transaction = _connection!.BeginTransaction();
            using var cmd = CreateSaveEditSettingsCommand();
            cmd.Transaction = transaction;
            foreach (var update in serializedUpdates)
            {
                ApplyUpdateParameters(cmd, update);
                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected != 1)
                {
                    throw new InvalidOperationException(
                        $"Catalog image {update.CatalogId} was not updated.");
                }
            }
            await transaction.CommitAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private Microsoft.Data.Sqlite.SqliteCommand CreateSaveEditSettingsCommand()
    {
        var cmd = _connection!.CreateCommand();
        cmd.CommandText = SaveEditSettingsSql;
        cmd.Parameters.Add("@id", Microsoft.Data.Sqlite.SqliteType.Integer);
        cmd.Parameters.Add("@editSettings", Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@editVersion", Microsoft.Data.Sqlite.SqliteType.Integer);
        cmd.Parameters.Add("@updated", Microsoft.Data.Sqlite.SqliteType.Text);
        return cmd;
    }

    private static void ApplyUpdateParameters(
        Microsoft.Data.Sqlite.SqliteCommand cmd,
        SerializedEditUpdate update)
    {
        cmd.Parameters["@id"].Value = update.CatalogId;
        cmd.Parameters["@editSettings"].Value = update.SettingsJson;
        cmd.Parameters["@editVersion"].Value = EditSettings.CurrentVersion;
        cmd.Parameters["@updated"].Value = DateTime.UtcNow.ToString("O");
    }

    private static SerializedEditUpdate SerializeUpdate(CatalogEditSettingsUpdate update)
    {
        return new SerializedEditUpdate(
            update.CatalogId,
            EditSettingsJson.Serialize(update.Settings));
    }

    private sealed record SerializedEditUpdate(
        long CatalogId,
        string SettingsJson);

    /// <summary>Saves the culling flag state for an image.</summary>
    public async Task SaveFlagStateAsync(long catalogId, ImageFlag flag)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync();
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                UPDATE images SET flag_state = @flagState, updated_utc = @updated
                WHERE id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", catalogId);
            cmd.Parameters.AddWithValue("@flagState", (int)flag);
            cmd.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <summary>Saves a star rating clamped to the range 0-5.</summary>
    public async Task SaveRatingAsync(long catalogId, int rating)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync();
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                UPDATE images SET rating = @rating, updated_utc = @updated
                WHERE id = @id;
            ";
            cmd.Parameters.AddWithValue("@id", catalogId);
            cmd.Parameters.AddWithValue("@rating", Math.Clamp(rating, 0, 5));
            cmd.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <summary>Saves one color label atomically for a distinct group of images.</summary>
    public async Task SaveColorLabelAsync(
        IReadOnlyCollection<long> catalogIds,
        ColorLabel colorLabel)
    {
        EnsureInitialized();
        var ids = catalogIds.Distinct().ToArray();
        if (ids.Length == 0) return;

        await _connectionGate.WaitAsync();
        try
        {
            using var transaction = _connection!.BeginTransaction();
            await WriteColorLabelAsync(
                _connection,
                transaction,
                ids,
                colorLabel);
            await transaction.CommitAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <summary>Writes labels using a caller-owned connection gate and transaction.</summary>
    internal static async Task WriteColorLabelAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<long> catalogIds,
        ColorLabel colorLabel)
    {
        var ids = catalogIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        if (!Enum.IsDefined(colorLabel))
            throw new ArgumentOutOfRangeException(nameof(colorLabel));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE images SET color_label = @colorLabel, updated_utc = @updated
            WHERE id IN (SELECT value FROM json_each(@ids));
            """;
        command.Parameters.AddWithValue("@colorLabel", (int)colorLabel);
        command.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("@ids", JsonSerializer.Serialize(ids));
        var affected = await command.ExecuteNonQueryAsync();
        if (affected != ids.Length)
        {
            throw new InvalidOperationException(
                $"Catalog label update expected {ids.Length} rows but updated {affected}.");
        }
    }

    /// <summary>Deletes an image and its associated catalog data.</summary>
    public async Task DeleteImageAsync(long catalogId)
    {
        EnsureInitialized();
        var thumbPath = GetThumbnailPath(catalogId);
        var previewPath = GetPreviewPath(catalogId);
        var previewMetadataPath = Path.ChangeExtension(previewPath, ".meta");
        var renderedThumbnailPath = GetRenderedThumbnailPath(catalogId);
        var renderedThumbnailMetadataPath =
            Path.ChangeExtension(renderedThumbnailPath, ".meta");
        if (File.Exists(thumbPath)) File.Delete(thumbPath);
        if (File.Exists(previewPath)) File.Delete(previewPath);
        if (File.Exists(previewMetadataPath)) File.Delete(previewMetadataPath);
        if (File.Exists(renderedThumbnailPath)) File.Delete(renderedThumbnailPath);
        if (File.Exists(renderedThumbnailMetadataPath))
            File.Delete(renderedThumbnailMetadataPath);

        await _connectionGate.WaitAsync();
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "DELETE FROM images WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", catalogId);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }
}

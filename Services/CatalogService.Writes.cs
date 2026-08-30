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

    /// <summary>Saves edit settings for an image without touching its history.</summary>
    public Task SaveEditSettingsAsync(long catalogId, EditSettings settings) =>
        SaveEditSettingsWithHistoryAsync(catalogId, settings, null);

    /// <summary>Saves edit settings atomically for a group of images without history rows.</summary>
    public Task SaveEditSettingsBatchAsync(IReadOnlyList<CatalogEditSettingsUpdate> updates) =>
        SaveEditSettingsBatchWithHistoryAsync(updates, operation: string.Empty);

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

    /// <summary>Deletes an image and its associated catalog data.</summary>
    public async Task DeleteImageAsync(long catalogId)
    {
        EnsureInitialized();
        DeleteCacheAssets(catalogId);

        await _connectionGate.WaitAsync();
        try
        {
            using var transaction = _connection!.BeginTransaction();
            using var cmd = _connection!.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText =
                "DELETE FROM image_assessments WHERE image_id = @id;";
            cmd.Parameters.AddWithValue("@id", catalogId);
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "DELETE FROM edit_history WHERE image_id = @id;";
            await cmd.ExecuteNonQueryAsync();
            cmd.CommandText = "DELETE FROM images WHERE id = @id;";
            await cmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private void DeleteCacheAssets(long catalogId)
    {
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
    }
}

using System.Text.Json;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public partial class CatalogService
{
    private const string SaveEditSettingsSql = @"
        UPDATE images SET
            exposure = @exposure,
            temperature = @temperature,
            brightness = @brightness,
            contrast = @contrast,
            saturation = @saturation,
            vibrance = @vibrance,
            shadows = @shadows,
            highlights = @highlights,
            rotation = @rotation,
            horizon_rotation = @horizonRotation,
            crop_data = @crop,
            curve_data = @curve,
            applied_preset_id = @presetId,
            updated_utc = @updated
        WHERE id = @id;
    ";

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
        cmd.Parameters.Add("@exposure", Microsoft.Data.Sqlite.SqliteType.Real);
        cmd.Parameters.Add("@temperature", Microsoft.Data.Sqlite.SqliteType.Integer);
        cmd.Parameters.Add("@brightness", Microsoft.Data.Sqlite.SqliteType.Integer);
        cmd.Parameters.Add("@contrast", Microsoft.Data.Sqlite.SqliteType.Integer);
        cmd.Parameters.Add("@saturation", Microsoft.Data.Sqlite.SqliteType.Integer);
        cmd.Parameters.Add("@vibrance", Microsoft.Data.Sqlite.SqliteType.Integer);
        cmd.Parameters.Add("@shadows", Microsoft.Data.Sqlite.SqliteType.Integer);
        cmd.Parameters.Add("@highlights", Microsoft.Data.Sqlite.SqliteType.Integer);
        cmd.Parameters.Add("@rotation", Microsoft.Data.Sqlite.SqliteType.Integer);
        cmd.Parameters.Add("@horizonRotation", Microsoft.Data.Sqlite.SqliteType.Real);
        cmd.Parameters.Add("@crop", Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@curve", Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@presetId", Microsoft.Data.Sqlite.SqliteType.Text);
        cmd.Parameters.Add("@updated", Microsoft.Data.Sqlite.SqliteType.Text);
        return cmd;
    }

    private static void ApplyUpdateParameters(
        Microsoft.Data.Sqlite.SqliteCommand cmd,
        SerializedEditUpdate update)
    {
        var settings = update.Settings;
        cmd.Parameters["@id"].Value = update.CatalogId;
        cmd.Parameters["@exposure"].Value = settings.Exposure;
        cmd.Parameters["@temperature"].Value = settings.Temperature;
        cmd.Parameters["@brightness"].Value = settings.Brightness;
        cmd.Parameters["@contrast"].Value = settings.Contrast;
        cmd.Parameters["@saturation"].Value = settings.Saturation;
        cmd.Parameters["@vibrance"].Value = settings.Vibrance;
        cmd.Parameters["@shadows"].Value = settings.Shadows;
        cmd.Parameters["@highlights"].Value = settings.Highlights;
        cmd.Parameters["@rotation"].Value = settings.Rotation;
        cmd.Parameters["@horizonRotation"].Value = settings.HorizonRotation;
        cmd.Parameters["@crop"].Value = update.CropJson ?? (object)DBNull.Value;
        cmd.Parameters["@curve"].Value = update.CurveJson ?? (object)DBNull.Value;
        cmd.Parameters["@presetId"].Value = settings.AppliedPresetId ?? (object)DBNull.Value;
        cmd.Parameters["@updated"].Value = DateTime.UtcNow.ToString("O");
    }

    private static SerializedEditUpdate SerializeUpdate(CatalogEditSettingsUpdate update)
    {
        var settings = update.Settings.Clone();
        return new SerializedEditUpdate(
            update.CatalogId,
            settings,
            settings.Crop != null && !settings.Crop.IsFullImage
                ? JsonSerializer.Serialize(settings.Crop, JsonOptions)
                : null,
            settings.Curve != null && !settings.Curve.IsIdentity()
                ? JsonSerializer.Serialize(settings.Curve, JsonOptions)
                : null);
    }

    private sealed record SerializedEditUpdate(
        long CatalogId,
        EditSettings Settings,
        string? CropJson,
        string? CurveJson);

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

    public async Task DeleteImageAsync(long catalogId)
    {
        EnsureInitialized();
        var thumbPath = GetThumbnailPath(catalogId);
        var previewPath = GetPreviewPath(catalogId);
        if (File.Exists(thumbPath)) File.Delete(thumbPath);
        if (File.Exists(previewPath)) File.Delete(previewPath);

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

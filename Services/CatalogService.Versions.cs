using HappyPhoton.Models;

namespace HappyPhoton.Services;

public partial class CatalogService
{
    public async Task<CatalogImageState?> CreateVersionAsync(long sourceCatalogId)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync();
        try
        {
            using var transaction = _connection!.BeginTransaction();
            using var command = _connection!.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                WITH RECURSIVE candidates(version) AS (
                    VALUES (2) UNION ALL SELECT version + 1
                    FROM candidates WHERE version < 8),
                next_version AS (
                    SELECT MIN(candidates.version) AS version
                    FROM candidates, images source
                    WHERE source.id = @sourceId AND NOT EXISTS (
                        SELECT 1 FROM images sibling
                        WHERE sibling.file_path = source.file_path COLLATE NOCASE
                          AND sibling.version = candidates.version))
                INSERT INTO images (
                    file_path, version, file_name, edit_settings,
                    edit_version, flag_state, rating, color_label, updated_utc)
                SELECT source.file_path, next_version.version, source.file_name,
                       source.edit_settings, source.edit_version, source.flag_state,
                       source.rating, source.color_label, @updated
                FROM images source, next_version
                WHERE source.id = @sourceId AND next_version.version IS NOT NULL
                RETURNING id, version, version_label, edit_settings, edit_version,
                          flag_state, rating, color_label;
                """;
            command.Parameters.AddWithValue("@sourceId", sourceCatalogId);
            command.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;
            var id = reader.GetInt64(0);
            var state = new CatalogImageState(
                id,
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                ReadEditSettings(reader, 3, id, $"version {reader.GetInt32(1)}"),
                ReadEnumColumn(reader, 5, ImageFlag.Unflagged),
                reader.IsDBNull(6)
                    ? 0
                    : (int)Math.Clamp(reader.GetInt64(6), 0, 5),
                ReadEnumColumn(reader, 7, ColorLabel.None));
            await reader.DisposeAsync();

            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO image_assessments (
                    image_id, revision, assessed_utc, pending_axes)
                SELECT @id, revision, assessed_utc, 0
                FROM image_assessments WHERE image_id = @sourceId;
                """;
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@sourceId", sourceCatalogId);
            await command.ExecuteNonQueryAsync();

            command.Parameters.Clear();
            command.CommandText = """
                SELECT revision, assessed_utc
                FROM image_assessments WHERE image_id = @id;
                """;
            command.Parameters.AddWithValue("@id", id);
            using (var assessment = await command.ExecuteReaderAsync())
            {
                if (await assessment.ReadAsync())
                {
                    state = state with
                    {
                        AssessmentRevision = assessment.GetInt64(0),
                        AssessedUtc = DateTime.Parse(
                            assessment.GetString(1),
                            null,
                            System.Globalization.DateTimeStyles.RoundtripKind)
                    };
                }
            }

            await transaction.CommitAsync();
            await RefreshCacheStampAsync(id);
            return state;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task RenameVersionAsync(long catalogId, string? label)
    {
        EnsureInitialized();
        var normalized = string.IsNullOrWhiteSpace(label)
            ? null
            : label.Trim();
        if (normalized?.Length > 24)
            throw new ArgumentException("Version labels are limited to 24 characters.");

        await _connectionGate.WaitAsync();
        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText = """
                UPDATE images SET version_label = @label, updated_utc = @updated
                WHERE id = @id;
                """;
            command.Parameters.AddWithValue("@id", catalogId);
            command.Parameters.AddWithValue("@label", (object?)normalized ?? DBNull.Value);
            command.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException($"Catalog image {catalogId} was not found.");
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<bool> DeleteVersionAsync(long catalogId)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync();
        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText = "SELECT version FROM images WHERE id = @id;";
            command.Parameters.AddWithValue("@id", catalogId);
            if ((long?)await command.ExecuteScalarAsync() is not > 1) return false;
        }
        finally
        {
            _connectionGate.Release();
        }
        await DeleteImageAsync(catalogId);
        return true;
    }

    public async Task DeleteFileAsync(string filePath)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync();
        try
        {
            using var transaction = _connection!.BeginTransaction();
            using var command = _connection!.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                "SELECT id FROM images WHERE file_path = @path COLLATE NOCASE;";
            command.Parameters.AddWithValue("@path", filePath);
            var found = new List<long>();
            using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) found.Add(reader.GetInt64(0));

            foreach (var id in found) DeleteCacheAssets(id);

            command.CommandText = """
                DELETE FROM image_assessments
                WHERE image_id IN (
                    SELECT id FROM images
                    WHERE file_path = @path COLLATE NOCASE);
                """;
            await command.ExecuteNonQueryAsync();
            command.CommandText = """
                DELETE FROM edit_history
                WHERE image_id IN (
                    SELECT id FROM images
                    WHERE file_path = @path COLLATE NOCASE);
                """;
            await command.ExecuteNonQueryAsync();
            command.CommandText =
                "DELETE FROM images WHERE file_path = @path COLLATE NOCASE;";
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        finally
        {
            _connectionGate.Release();
        }
    }
}

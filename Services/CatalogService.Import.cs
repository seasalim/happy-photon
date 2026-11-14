using HappyPhoton.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace HappyPhoton.Services;

public partial class CatalogService
{
    internal Func<int, CancellationToken, Task>? ImportWriteObserverAsync { get; set; }

    internal async Task<IReadOnlyDictionary<string, CatalogImportBaseline>>
        LoadImportBaselinesAsync(
            IReadOnlyCollection<string> filePaths,
            CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (filePaths.Count == 0)
            return new Dictionary<string, CatalogImportBaseline>();
        return await GatedAsync(async connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT images.file_path, images.id, images.flag_state,
                       images.rating, images.color_label,
                       image_assessments.revision,
                       image_assessments.assessed_utc,
                       image_assessments.pending_axes, images.edit_settings,
                       images.edit_version
                FROM json_each(@paths) requested
                JOIN images ON images.file_path = requested.value COLLATE NOCASE
                           AND images.version = 1
                LEFT JOIN image_assessments
                  ON image_assessments.image_id = images.id;
                """;
            command.Parameters.AddWithValue(
                "@paths", JsonSerializer.Serialize(filePaths));
            // Match the catalog's COLLATE NOCASE file_path identity.
            var result = new Dictionary<string, CatalogImportBaseline>(
                StringComparer.OrdinalIgnoreCase);
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                result[reader.GetString(0)] = ReadImportBaseline(reader, 1, 0);
            }
            return result;
        }, cancellationToken);
    }

    public async Task<CatalogImportApplyResult> ApplyImportAsync(
        CatalogImportPreview preview,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return await GatedAsync(async connection =>
        {
            using var transaction = connection.BeginTransaction();
            var writes = 0;
            var adoptions = new List<CatalogImportAdoption>();

            foreach (var change in preview.Changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = await ReadImportBaselineAsync(
                    connection, transaction, change.FilePath, cancellationToken);
                if (!BaselineMatches(change.Baseline, current, change.ComparedAxes))
                    throw new CatalogImportConflictException();

                var imageId = current.ImageId;
                if (!current.Exists)
                {
                    imageId = await InsertImportedPathAsync(
                        connection, transaction, change.FilePath, cancellationToken);
                    await ObserveImportWriteAsync(++writes, cancellationToken);
                }

                var adoptedCrop = await AdoptImportedCropAsync(
                    transaction, imageId, current.EditSettings ?? new EditSettings(),
                    change.Crop, cancellationToken);
                if (adoptedCrop != null)
                    await ObserveImportWriteAsync(++writes, cancellationToken);
                if (change.Axes == AssessmentAxes.None && adoptedCrop == null) continue;
                if (change.Axes == AssessmentAxes.None)
                    await EnsureAssessmentRowsAsync(connection, [imageId], cancellationToken, transaction);
                else
                    await UpdateImportedAssessmentAsync(connection, transaction, imageId, change, DateTime.UtcNow, cancellationToken);
                if (change.Axes != AssessmentAxes.None) await ObserveImportWriteAsync(++writes, cancellationToken);

                var snapshot = (await ReadAssessmentSnapshotsAsync(
                    connection, transaction, [imageId], cancellationToken)).Single();
                adoptions.Add(new CatalogImportAdoption(
                    current.Revision, snapshot, adoptedCrop));
            }

            var currentSettings = await ReadSettingAsync(
                connection, transaction, preview.SettingsKey, cancellationToken);
            if (!string.Equals(currentSettings, preview.BaselineSettingsJson,
                    StringComparison.Ordinal))
            {
                throw new CatalogImportConflictException();
            }
            if (!string.Equals(currentSettings, preview.SettingsJson,
                    StringComparison.Ordinal))
            {
                await WriteSettingAsync(
                    connection, transaction, preview.SettingsKey,
                    preview.SettingsJson, cancellationToken);
                await ObserveImportWriteAsync(++writes, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new CatalogImportApplyResult(preview.Report, adoptions, writes);
        }, cancellationToken);
    }

    private async Task ObserveImportWriteAsync(
        int writes,
        CancellationToken cancellationToken)
    {
        if (ImportWriteObserverAsync != null)
            await ImportWriteObserverAsync(writes, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool BaselineMatches(
        CatalogImportBaseline expected,
        CatalogImportBaseline current,
        AssessmentAxes axes)
    {
        if (expected.Exists != current.Exists) return false;
        if (!expected.Exists) return true;
        return (!axes.HasFlag(AssessmentAxes.Rating) ||
                expected.Rating == current.Rating) &&
               (!axes.HasFlag(AssessmentAxes.Flag) ||
                expected.Flag == current.Flag) &&
               (!axes.HasFlag(AssessmentAxes.Label) ||
                expected.ColorLabel == current.ColorLabel);
    }

    private async Task<CatalogImportBaseline> ReadImportBaselineAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string filePath,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT images.id, images.flag_state, images.rating, images.color_label,
                   image_assessments.revision, image_assessments.assessed_utc,
                   image_assessments.pending_axes, images.file_path,
                   images.edit_settings, images.edit_version
            FROM images
            LEFT JOIN image_assessments ON image_assessments.image_id = images.id
            WHERE images.file_path = @path COLLATE NOCASE
              AND images.version = 1;
            """;
        command.Parameters.AddWithValue("@path", filePath);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new CatalogImportBaseline(
                false, 0, ImageFlag.Unflagged, 0, ColorLabel.None,
                0, null, AssessmentAxes.None, null);
        }

        return ReadImportBaseline(reader, 0, 7);
    }

    // Reads id, flag, rating, label, revision, assessed time and pending axes from consecutive
    // columns starting at idOrdinal; the edit settings are always columns 8 and 9.
    private CatalogImportBaseline ReadImportBaseline(
        SqliteDataReader reader, int idOrdinal, int pathOrdinal)
    {
        var id = reader.GetInt64(idOrdinal);
        var path = reader.GetString(pathOrdinal);

        return new CatalogImportBaseline(
            true, id,
            ReadEnumColumn(reader, idOrdinal + 1, ImageFlag.Unflagged),
            ReadRating(reader, idOrdinal + 2),
            ReadEnumColumn(reader, idOrdinal + 3, ColorLabel.None),
            reader.IsDBNull(idOrdinal + 4) ? 0 : reader.GetInt64(idOrdinal + 4),
            ReadUtcOrNull(reader, idOrdinal + 5),
            reader.IsDBNull(idOrdinal + 6) ? AssessmentAxes.None :
                (AssessmentAxes)reader.GetInt32(idOrdinal + 6),
            path,
            ReadEditSettings(reader, 8, id, path));
    }

    private async Task<CropRegion?> AdoptImportedCropAsync(
        SqliteTransaction transaction,
        long imageId,
        EditSettings current,
        CropRegion? crop,
        CancellationToken cancellationToken)
    {
        if (crop == null || XmpCropProjection.HasGeometryEdits(current)) return null;
        var updated = current.Clone();
        updated.Crop = crop.Clone();
        var history = await ReadHistoryAsync(_connection!, transaction, imageId);
        var mutation = CatalogEditHistory.PrepareAppend(
            history, current, updated, "Crop from Lightroom");
        await WriteSettingsAsync(transaction, SerializeUpdate(new(imageId, updated)));
        if (mutation != null)
            await WriteHistoryMutationAsync(transaction, imageId, mutation);
        cancellationToken.ThrowIfCancellationRequested();
        return crop.Clone();
    }

    private static async Task<long> InsertImportedPathAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string filePath,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO images (
                file_path, version, file_name, edit_settings, edit_version, updated_utc)
            VALUES (@path, 1, @name, @editSettings, @editVersion, @updated)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("@path", filePath);
        command.Parameters.AddWithValue("@name", Path.GetFileName(filePath));
        command.Parameters.AddWithValue("@editSettings", DefaultEditSettingsJson);
        command.Parameters.AddWithValue("@editVersion", EditSettings.CurrentVersion);
        command.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task UpdateImportedAssessmentAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long imageId,
        CatalogImportChange change,
        DateTime assessedUtc,
        CancellationToken cancellationToken)
    {
        using var image = connection.CreateCommand();
        image.Transaction = transaction;
        var assignments = new List<string>();
        if (change.Axes.HasFlag(AssessmentAxes.Rating))
        {
            assignments.Add("rating = @rating");
            image.Parameters.AddWithValue("@rating", change.Rating!.Value);
        }
        if (change.Axes.HasFlag(AssessmentAxes.Flag))
        {
            assignments.Add("flag_state = @flag");
            image.Parameters.AddWithValue("@flag", (int)change.Flag!.Value);
        }
        if (change.Axes.HasFlag(AssessmentAxes.Label))
        {
            assignments.Add("color_label = @label");
            image.Parameters.AddWithValue("@label", (int)change.ColorLabel!.Value);
        }
        assignments.Add("updated_utc = @updated");
        image.CommandText =
            $"UPDATE images SET {string.Join(", ", assignments)} WHERE id = @id;";
        image.Parameters.AddWithValue("@id", imageId);
        image.Parameters.AddWithValue("@updated", assessedUtc.ToString("O"));
        if (await image.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Catalog image {imageId} was not updated.");

        using var assessment = connection.CreateCommand();
        assessment.Transaction = transaction;
        assessment.CommandText = """
            INSERT INTO image_assessments (
                image_id, revision, assessed_utc, pending_axes)
            VALUES (@id, 1, @assessedUtc, 0)
            ON CONFLICT(image_id) DO UPDATE SET
                revision = revision + 1,
                assessed_utc = excluded.assessed_utc;
            """;
        assessment.Parameters.AddWithValue("@id", imageId);
        assessment.Parameters.AddWithValue("@assessedUtc", assessedUtc.ToString("O"));
        await assessment.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> ReadSettingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM app_settings WHERE key = @key;";
        command.Parameters.AddWithValue("@key", key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task WriteSettingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO app_settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

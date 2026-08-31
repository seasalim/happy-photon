using System.Text.Json;
using HappyPhoton.Models;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public partial class CatalogService
{
    public async Task<IReadOnlyList<AssessmentSnapshot>> MutateAssessmentsAsync(
        IReadOnlyCollection<AssessmentMutation> mutations,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var normalized = mutations
            .GroupBy(mutation => mutation.ImageId)
            .Select(group => group.Last())
            .ToArray();
        if (normalized.Length == 0) return [];
        ValidateMutations(normalized);

        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            using var transaction = _connection!.BeginTransaction();
            var assessedUtc = DateTime.UtcNow;
            foreach (var mutation in normalized)
            {
                await ApplyAssessmentMutationAsync(
                    _connection, transaction, mutation,
                    mutation.PendingAxes & mutation.Axes,
                    assessedUtc,
                    cancellationToken);
            }

            var snapshots = await ReadAssessmentSnapshotsAsync(
                _connection, transaction,
                normalized.Select(mutation => mutation.ImageId).ToArray(),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return snapshots;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<IReadOnlyList<AssessmentSnapshot>> LoadAssessmentSnapshotsAsync(
        IReadOnlyCollection<long> imageIds,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var ids = imageIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureAssessmentRowsAsync(
                _connection!, ids, cancellationToken);
            return await ReadAssessmentSnapshotsAsync(
                _connection!, null, ids, cancellationToken);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private static async Task EnsureAssessmentRowsAsync(
        SqliteConnection connection,
        IReadOnlyCollection<long> imageIds,
        CancellationToken cancellationToken, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO image_assessments (
                image_id, revision, assessed_utc, pending_axes)
            SELECT images.id, 0, @epoch, 0
            FROM images
            WHERE images.id IN (SELECT value FROM json_each(@ids))
            ON CONFLICT(image_id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("@ids", JsonSerializer.Serialize(imageIds));
        command.Parameters.AddWithValue("@epoch", DateTime.UnixEpoch.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> ClearPendingAxesAsync(
        long imageId,
        long revision,
        AssessmentAxes axes,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText = """
                UPDATE image_assessments
                SET pending_axes = pending_axes & ~@axes
                WHERE image_id = @id AND revision = @revision;
                """;
            command.Parameters.AddWithValue("@id", imageId);
            command.Parameters.AddWithValue("@revision", revision);
            command.Parameters.AddWithValue("@axes", (int)axes);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<XmpCropProjection> LoadCropProjectionAsync(
        long imageId,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            using var command = _connection!.CreateCommand();
            command.CommandText =
                "SELECT edit_settings FROM images WHERE id = @id AND version = 1;";
            command.Parameters.AddWithValue("@id", imageId);
            var json = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (json == null)
                throw new InvalidOperationException(
                    $"Catalog image {imageId} was not found or is not V1.");
            return XmpCropProjection.From(EditSettingsJson.Deserialize(json, out _));
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<IReadOnlyList<XmpReconcileAdoption>> AdoptSidecarFactsAsync(
        IReadOnlyCollection<XmpReconcileItem> items,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        if (items.Count == 0) return [];
        await _connectionGate.WaitAsync(cancellationToken);
        try
        {
            using var transaction = _connection!.BeginTransaction();
            var adopted = new List<XmpReconcileAdoption>();
            foreach (var item in items)
            {
                var axes = SelectAdoptableAxes(item);
                if (axes == AssessmentAxes.None) continue;
                var adoptedAxes = await AdoptOneAsync(
                    _connection, transaction, item, axes, cancellationToken);
                if (adoptedAxes != AssessmentAxes.None)
                {
                    var refreshed = (await ReadAssessmentSnapshotsAsync(
                        _connection, transaction, [item.Snapshot.ImageId],
                        cancellationToken)).Single();
                    adopted.Add(new XmpReconcileAdoption(
                        refreshed, adoptedAxes,
                        adoptedAxes.HasFlag(AssessmentAxes.Crop)
                            ? item.Facts.Crop.Value.Clone()
                            : null));
                }
            }
            await transaction.CommitAsync(cancellationToken);
            return adopted;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private static AssessmentAxes SelectAdoptableAxes(XmpReconcileItem item)
    {
        var pending = item.Snapshot.PendingAxes;
        var axes = AssessmentAxes.None;
        if (item.Facts.Crop.Kind == XmpFactKind.Matched &&
            !pending.HasFlag(AssessmentAxes.Crop))
        {
            axes |= AssessmentAxes.Crop;
        }
        if (item.Snapshot.AssessedUtc.AddSeconds(2) >= item.Sidecar.LastWriteUtc)
            return axes;
        if (item.Facts.Rating.CanAdopt && !pending.HasFlag(AssessmentAxes.Rating))
            axes |= AssessmentAxes.Rating;
        var canAdoptFlag = item.Facts.Flag.CanAdopt ||
            item.Facts.Flag.Kind == XmpFactKind.WeakClear &&
            item.Snapshot.Flag == ImageFlag.Rejected;
        if (canAdoptFlag && !pending.HasFlag(AssessmentAxes.Flag))
            axes |= AssessmentAxes.Flag;
        if (item.Facts.Label.CanAdopt && !pending.HasFlag(AssessmentAxes.Label))
            axes |= AssessmentAxes.Label;
        return axes;
    }

    private async Task<AssessmentAxes> AdoptOneAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        XmpReconcileItem item,
        AssessmentAxes axes,
        CancellationToken cancellationToken)
    {
        EditSettings? currentSettings = null;
        if (axes.HasFlag(AssessmentAxes.Crop))
        {
            currentSettings = await ReadAdoptableCropSettingsAsync(
                connection, transaction, item, axes, cancellationToken);
            if (currentSettings == null ||
                XmpCropProjection.HasGeometryEdits(currentSettings))
            {
                axes &= ~AssessmentAxes.Crop;
            }
        }
        if (axes == AssessmentAxes.None) return AssessmentAxes.None;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var assignments = new List<string>();
        if (axes.HasFlag(AssessmentAxes.Rating))
        {
            assignments.Add("rating = @rating");
            command.Parameters.AddWithValue("@rating", item.Facts.Rating.Value);
        }
        if (axes.HasFlag(AssessmentAxes.Flag))
        {
            assignments.Add("flag_state = @flag");
            command.Parameters.AddWithValue("@flag", (int)item.Facts.Flag.Value);
        }
        if (axes.HasFlag(AssessmentAxes.Label))
        {
            assignments.Add("color_label = @label");
            command.Parameters.AddWithValue("@label", (int)item.Facts.Label.Value);
        }
        assignments.Add("updated_utc = @updated");
        command.CommandText = $"""
            UPDATE images SET {string.Join(", ", assignments)}
            WHERE id = @id AND version = 1 AND EXISTS (
                SELECT 1 FROM image_assessments
                WHERE image_id = @id AND revision = @revision
                  AND (pending_axes & @axes) = 0);
            """;
        command.Parameters.AddWithValue("@id", item.Snapshot.ImageId);
        command.Parameters.AddWithValue("@revision", item.Snapshot.Revision);
        command.Parameters.AddWithValue("@axes", (int)axes);
        command.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            return AssessmentAxes.None;

        if (axes.HasFlag(AssessmentAxes.Crop))
        {
            var updatedSettings = currentSettings!.Clone();
            updatedSettings.Crop = item.Facts.Crop.Value.Clone();
            var history = await ReadHistoryAsync(
                connection, transaction, item.Snapshot.ImageId);
            var mutation = CatalogEditHistory.PrepareAppend(
                history, currentSettings, updatedSettings, "Crop from XMP");
            await WriteSettingsAsync(
                transaction, SerializeUpdate(new(
                    item.Snapshot.ImageId, updatedSettings)));
            if (mutation != null)
            {
                await WriteHistoryMutationAsync(
                    transaction, item.Snapshot.ImageId, mutation);
            }
        }

        command.Parameters.Clear();
        command.CommandText = """
            UPDATE image_assessments
            SET revision = revision + 1, assessed_utc = @assessedUtc
            WHERE image_id = @id AND revision = @revision;
            """;
        command.Parameters.AddWithValue("@id", item.Snapshot.ImageId);
        command.Parameters.AddWithValue("@revision", item.Snapshot.Revision);
        command.Parameters.AddWithValue(
            "@assessedUtc",
            (item.Sidecar.LastWriteUtc > item.Snapshot.AssessedUtc
                ? item.Sidecar.LastWriteUtc
                : item.Snapshot.AssessedUtc).ToString("O"));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1
            ? axes
            : AssessmentAxes.None;
    }

    private static async Task<EditSettings?> ReadAdoptableCropSettingsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        XmpReconcileItem item,
        AssessmentAxes axes,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT images.edit_settings
            FROM images
            JOIN image_assessments ON image_assessments.image_id = images.id
            WHERE images.id = @id AND images.version = 1
              AND image_assessments.revision = @revision
              AND (image_assessments.pending_axes & @axes) = 0;
            """;
        command.Parameters.AddWithValue("@id", item.Snapshot.ImageId);
        command.Parameters.AddWithValue("@revision", item.Snapshot.Revision);
        command.Parameters.AddWithValue("@axes", (int)axes);
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return json == null ? null : EditSettingsJson.Deserialize(json, out _);
    }

    private static async Task ApplyAssessmentMutationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssessmentMutation mutation,
        AssessmentAxes pendingAxes,
        DateTime assessedUtc,
        CancellationToken cancellationToken)
    {
        using var image = connection.CreateCommand();
        image.Transaction = transaction;
        var assignments = new List<string>();
        if (mutation.Axes.HasFlag(AssessmentAxes.Rating))
        {
            assignments.Add("rating = @rating");
            image.Parameters.AddWithValue("@rating", Math.Clamp(mutation.Rating!.Value, 0, 5));
        }
        if (mutation.Axes.HasFlag(AssessmentAxes.Flag))
        {
            assignments.Add("flag_state = @flag");
            image.Parameters.AddWithValue("@flag", (int)mutation.Flag!.Value);
        }
        if (mutation.Axes.HasFlag(AssessmentAxes.Label))
        {
            assignments.Add("color_label = @label");
            image.Parameters.AddWithValue("@label", (int)mutation.ColorLabel!.Value);
        }
        assignments.Add("updated_utc = @updated");
        image.CommandText = $"UPDATE images SET {string.Join(", ", assignments)} WHERE id = @id;";
        image.Parameters.AddWithValue("@id", mutation.ImageId);
        image.Parameters.AddWithValue("@updated", assessedUtc.ToString("O"));
        if (await image.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Catalog image {mutation.ImageId} was not updated.");

        using var assessment = connection.CreateCommand();
        assessment.Transaction = transaction;
        assessment.CommandText = """
            INSERT INTO image_assessments (
                image_id, revision, assessed_utc, pending_axes)
            SELECT @id, 1, @assessedUtc,
                   CASE WHEN images.version = 1 THEN @pendingAxes ELSE 0 END
            FROM images WHERE images.id = @id
            ON CONFLICT(image_id) DO UPDATE SET
                revision = revision + 1,
                assessed_utc = excluded.assessed_utc,
                pending_axes = pending_axes | excluded.pending_axes;
            """;
        assessment.Parameters.AddWithValue("@id", mutation.ImageId);
        assessment.Parameters.AddWithValue("@assessedUtc", assessedUtc.ToString("O"));
        assessment.Parameters.AddWithValue("@pendingAxes", (int)pendingAxes);
        await assessment.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<AssessmentSnapshot>> ReadAssessmentSnapshotsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        IReadOnlyCollection<long> imageIds,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT images.id, images.file_path, images.flag_state, images.rating,
                   images.color_label, image_assessments.revision,
                   image_assessments.assessed_utc, image_assessments.pending_axes
            FROM images
            JOIN image_assessments ON image_assessments.image_id = images.id
            WHERE images.id IN (SELECT value FROM json_each(@ids));
            """;
        command.Parameters.AddWithValue("@ids", JsonSerializer.Serialize(imageIds));
        var result = new List<AssessmentSnapshot>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AssessmentSnapshot(
                reader.GetInt64(0), reader.GetString(1),
                (ImageFlag)reader.GetInt32(2), reader.GetInt32(3),
                (ColorLabel)reader.GetInt32(4), reader.GetInt64(5),
                DateTime.Parse(reader.GetString(6), null,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                (AssessmentAxes)reader.GetInt32(7)));
        }
        if (result.Count != imageIds.Distinct().Count())
            throw new InvalidOperationException("One or more catalog assessments were not found.");
        return result;
    }

    private static void ValidateMutations(IEnumerable<AssessmentMutation> mutations)
    {
        foreach (var mutation in mutations)
        {
            if (mutation.Axes == AssessmentAxes.None ||
                mutation.Axes.HasFlag(AssessmentAxes.Rating) && mutation.Rating == null ||
                mutation.Axes.HasFlag(AssessmentAxes.Flag) &&
                (mutation.Flag == null || !Enum.IsDefined(mutation.Flag.Value)) ||
                mutation.Axes.HasFlag(AssessmentAxes.Label) &&
                (mutation.ColorLabel == null || !Enum.IsDefined(mutation.ColorLabel.Value)))
            {
                throw new ArgumentException("Assessment mutation is incomplete or invalid.");
            }
        }
    }
}

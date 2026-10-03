using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogBatchUndoTests
{
    [Fact]
    public async Task RestorationSnapshotUsesTheDocumentInsideTheTransaction()
    {
        using var fixture = new CatalogVmFixture("batch-stale-snapshot");
        using var catalog = await fixture.CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(fixture.Path("target.jpg"));
        var stored = new EditSettings { Exposure = .5 };
        await catalog.SaveEditSettingsAsync(id, stored);
        var batch = await catalog.SaveEditSettingsBatchWithHistoryAsync(
            [new(id, new() { Exposure = 2 }, new() { Exposure = 1 })], "Paste settings");

        Assert.Equal(EditSettingsJson.Serialize(stored), EditSettingsJson.Serialize(Assert.Single(batch).Previous));
        Assert.Equal([id], (await catalog.UndoEditSettingsBatchAsync(batch)).Restored);
        var persisted = await catalog.LoadImageStatesAsync([fixture.Path("target.jpg")]);
        Assert.Equal(EditSettingsJson.Serialize(batch[0].Previous),
            EditSettingsJson.Serialize(Assert.Single(persisted[fixture.Path("target.jpg")]).EditSettings));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("invalid")]
    [InlineData("valid")]
    public async Task BatchWithoutPreviousSnapshotPreservesStoredDocument(string kind)
    {
        using var fixture = new CatalogVmFixture("batch-without-snapshot");
        using var catalog = await fixture.CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(fixture.Path("target.jpg"));

        if (kind == "valid")
        {
            await catalog.SaveEditSettingsAsync(id, new() { Exposure = .5 });
        }
        else if (kind == "invalid")
        {
            await SqlAsync(catalog, "UPDATE images SET edit_settings = 'invalid';");
        }

        var original = await RawAsync(catalog, id);
        var batch = await catalog.SaveEditSettingsBatchWithHistoryAsync([new(id, new() { Exposure = 1 })], "");
        Assert.Equal(kind == "valid" ? .5 : 0, Assert.Single(batch).Previous.Exposure);
        Assert.Equal([id], (await catalog.UndoEditSettingsBatchAsync(batch)).Restored);
        Assert.Equal(original, await RawAsync(catalog, id));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("matching")]
    [InlineData("mismatch")]
    [InlineData("redo-tail")]
    [InlineData("marker")]
    public async Task RoundTripRestoresExactDocumentPositionAndHistoryBytes(string kind)
    {
        using var fixture = new CatalogVmFixture("batch-undo-bytes");
        using var catalog = await fixture.CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(fixture.Path("target.jpg"));
        var before = new EditSettings { Exposure = .5 };

        if (kind != "empty")
        {
            await catalog.SaveEditSettingsWithHistoryAsync(id, before, null, before: new EditSettings());

            if (kind == "redo-tail")
            {
                await catalog.SaveEditSettingsWithHistoryAsync(id, new() { Exposure = 2 }, null, before: before);
                await catalog.SaveEditSettingsWithHistoryAsync(id, before, null, position: 1);
            }

            if (kind == "mismatch") before.Contrast = 7;
        }

        await catalog.SaveEditSettingsAsync(id, before);
        // Whitespace is deliberately noncanonical: undo must restore stored bytes, not reserialize.
        await SqlAsync(catalog, "UPDATE images SET edit_settings = ' ' || edit_settings; " +
            "UPDATE edit_history SET settings_json = ' ' || settings_json;");
        var original = await RawAsync(catalog, id);
        var after = before.Clone();

        if (kind == "marker") after.AppliedPresetId = "user_marker";
        else after.Exposure = 1;

        var batch = await catalog.SaveEditSettingsBatchWithHistoryAsync([new(id, after, before)], "Paste settings");
        Assert.Equal(kind == "marker", Assert.Single(batch).Appended.Count == 0);
        var result = await catalog.UndoEditSettingsBatchAsync(batch);

        Assert.Equal([id], result.Restored);
        Assert.Empty(result.Skipped);
        Assert.Equal(original, await RawAsync(catalog, id));
    }

    [Theory]
    [InlineData("edit", false)]
    [InlineData("undo", false)]
    [InlineData("undo-twice", false)]
    [InlineData("jump", false)]
    [InlineData("clear", false)]
    [InlineData("reused-sequence", false)]
    [InlineData("reused-same-settings", false)]
    [InlineData("changed-original", false)]
    [InlineData("redo", true)]
    [InlineData("settings-only-write", false)]
    [InlineData("deleted", false)]
    public async Task EligibilityRequiresTheUnchangedBatchState(string action, bool eligible)
    {
        using var fixture = new CatalogVmFixture("batch-undo-eligibility");
        using var catalog = await fixture.CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(fixture.Path("target.jpg"));
        var before = new EditSettings { Exposure = .5 };
        var after = new EditSettings { Exposure = 1 };
        await catalog.SaveEditSettingsWithHistoryAsync(id, before, null, before: new EditSettings());
        var batch = await catalog.SaveEditSettingsBatchWithHistoryAsync([new(id, after, before)], "Paste settings");

        switch (action)
        {
            case "edit":
                await catalog.SaveEditSettingsWithHistoryAsync(id, new() { Exposure = 2 }, null, before: after);
                break;

            case "undo":
            case "undo-twice":
            case "jump":
            case "redo":
                var position = action == "undo" ? 1 : 0;
                await catalog.SaveEditSettingsWithHistoryAsync(id, position == 1 ? before : new(), null, position);

                if (action == "redo")
                {
                    await catalog.SaveEditSettingsWithHistoryAsync(id, after, null, position: 2);
                }

                break;

            case "clear":
                await catalog.ClearEditHistoryAsync(id);
                break;

            case "reused-sequence":
            case "reused-same-settings":
                await catalog.SaveEditSettingsWithHistoryAsync(id, before, null, position: 1);
                await catalog.SaveEditSettingsWithHistoryAsync(id,
                    action == "reused-same-settings" ? after : new() { Exposure = 2 },
                    null, before: before, historyLabel: "Exposure");
                break;

            case "changed-original":
                // Check every appended row, not just the newest: seed a two-row batch below.
                await catalog.ClearEditHistoryAsync(id);
                await catalog.SaveEditSettingsAsync(id, before);
                batch = await catalog.SaveEditSettingsBatchWithHistoryAsync([new(id, after, before)], "Paste settings");
                await SqlAsync(catalog, "UPDATE edit_history SET label = 'Other' WHERE seq = 0;");
                break;

            case "settings-only-write":
                await catalog.SaveEditSettingsAsync(id, new() { Exposure = 3 });
                break;

            case "deleted":
                await catalog.DeleteImageAsync(id);
                break;
        }

        var priorUndo = await RawAsync(catalog, id);
        var result = await catalog.UndoEditSettingsBatchAsync(batch);
        Assert.Equal(eligible ? new[] { id } : [], result.Restored);
        Assert.Equal(eligible ? [] : new[] { id }, result.Skipped);

        if (!eligible) Assert.Equal(priorUndo, await RawAsync(catalog, id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarkerOnlySkipsChangedHistoryOrSettings(bool changeHistory)
    {
        using var fixture = new CatalogVmFixture("batch-marker-skip");
        using var catalog = await fixture.CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(fixture.Path("target.jpg"));
        var before = new EditSettings { Exposure = 1 };
        await catalog.SaveEditSettingsWithHistoryAsync(id, before, null, before: new());
        var after = before.Clone();
        after.AppliedPresetId = "user_marker";
        var batch = await catalog.SaveEditSettingsBatchWithHistoryAsync([new(id, after, before)], "Paste settings");

        if (changeHistory) await catalog.ClearEditHistoryAsync(id);
        else await catalog.SaveEditSettingsAsync(id, before);

        var unchanged = await RawAsync(catalog, id);
        var result = await catalog.UndoEditSettingsBatchAsync(batch);
        Assert.Equal([id], result.Skipped);
        Assert.Equal(unchanged, await RawAsync(catalog, id));
    }

    [Fact]
    public async Task RacingEditBeforeUndoTransactionIsNotOverwritten()
    {
        using var fixture = new CatalogVmFixture("batch-undo-race");
        using var catalog = await fixture.CreateCatalogAsync();
        var id = await catalog.GetOrCreateImageAsync(fixture.Path("target.jpg"));
        var after = new EditSettings { Exposure = 1 };
        var batch = await catalog.SaveEditSettingsBatchWithHistoryAsync([new(id, after, new())], "Paste settings");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.EditHistoryWriteGateAsync = () =>
        {
            catalog.EditHistoryWriteGateAsync = null;
            entered.SetResult();

            return release.Task;
        };
        var undo = catalog.UndoEditSettingsBatchAsync(batch);

        try
        {
            await entered.Task.WaitAsync(TestWaits.Condition);
            await catalog.SaveEditSettingsWithHistoryAsync(id, new() { Exposure = 2 }, null, before: after);
            var edited = await RawAsync(catalog, id);
            release.SetResult();
            Assert.Equal([id], (await undo).Skipped);
            Assert.Equal(edited, await RawAsync(catalog, id));
        }
        finally
        {
            release.TrySetResult();
            await undo;
        }
    }

    [Fact]
    public async Task UndoRollsBackEveryTargetWhenARestoreFails()
    {
        using var fixture = new CatalogVmFixture("batch-undo-atomic");
        using var catalog = await fixture.CreateCatalogAsync();
        var first = await catalog.GetOrCreateImageAsync(fixture.Path("first.jpg"));
        var second = await catalog.GetOrCreateImageAsync(fixture.Path("second.jpg"));
        var batch = await catalog.SaveEditSettingsBatchWithHistoryAsync(
            [new(first, new() { Exposure = 1 }, new()), new(second, new() { Exposure = 1 }, new())], "Paste settings");
        var firstBefore = await RawAsync(catalog, first);
        var secondBefore = await RawAsync(catalog, second);
        await SqlAsync(catalog, $"""
            CREATE TRIGGER reject_restore BEFORE UPDATE ON images WHEN NEW.id = {second}
            BEGIN SELECT RAISE(ABORT, 'injected restore failure'); END;
            """);

        await Assert.ThrowsAsync<SqliteException>(() => catalog.UndoEditSettingsBatchAsync(batch));

        Assert.Equal(firstBefore, await RawAsync(catalog, first));
        Assert.Equal(secondBefore, await RawAsync(catalog, second));
    }

    internal static async Task<string[]> RawAsync(CatalogService catalog, long id)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(catalog.CatalogPath, "catalog.db")};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT edit_settings, edit_version, history_position FROM images WHERE id = @id;
            SELECT seq, label, settings_json FROM edit_history WHERE image_id = @id ORDER BY seq;
            """;
        command.Parameters.AddWithValue("@id", id);
        using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();

        do
        {
            while (await reader.ReadAsync())
            {
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    values.Add(reader.GetValue(column).ToString()!);
                }
            }
        } while (await reader.NextResultAsync());

        return values.ToArray();
    }

    private static async Task SqlAsync(CatalogService catalog, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(catalog.CatalogPath, "catalog.db")};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

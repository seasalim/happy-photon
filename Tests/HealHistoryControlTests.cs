using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

// Frozen G2 protocol and allowance: docs/pipeline/TESTING.md#heal-wp2-frozen-controls (section 5).
public sealed class HealHistoryControlTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ExposureControl()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("HEAL_CONTROL") == "1",
            "Opt-in frozen pre-change control; never recalculate the G2 allowance.");
        var result = await HealHistoryWorkload.RunAsync((settings, step) =>
            settings.Exposure = step / 16d);
        output.WriteLine($"G2 control: {result}");
        Assert.Equal(65, result.Rows);
    }
}

// Reuse this exact starting state and commit path for S64, changing only the
// callback to append one spot per step. The recorded control is never updated.
internal static class HealHistoryWorkload
{
    internal const long FrozenControlHistoryBytes = 32938;

    internal sealed record Measurement(long HistoryBytes, long Rows,
        long DatabaseBefore, long DatabaseAfter)
    {
        public long DatabaseGrowth => DatabaseAfter - DatabaseBefore;
    }

    internal static async Task<Measurement> RunAsync(Action<EditSettings, int> edit)
    {
        var root = Path.Combine(Path.GetTempPath(), $"heal-history-{Guid.NewGuid():N}");
        try
        {
            using var catalog = new CatalogService(root);
            await catalog.InitializeAsync();
            var id = await catalog.GetOrCreateImageAsync(Path.Combine(root, "synthetic.cr2"));
            var stored = await catalog.LoadEditHistoryAsync(id);
            Assert.Empty(stored.Entries);
            Assert.Equal(-1, stored.Position);
            var history = new EditHistory();
            history.Load(stored.Entries, stored.Position);
            var settings = new EditSettings();
            await using var connection = new SqliteConnection(
                $"Data Source={Path.Combine(root, "catalog.db")};Pooling=False");
            await connection.OpenAsync();
            var beforeBytes = await CountAsync(connection, id);
            var beforeFile = await CheckpointSizeAsync(connection, root);
            for (var step = 1; step <= 64; step++)
            {
                var before = settings.Clone();
                edit(settings, step);
                // MainWindowViewModel.EditSaving uses these same three calls.
                var mutation = history.PrepareAppend(before, settings);
                Assert.NotNull(mutation);
                await catalog.SaveEditSettingsWithHistoryAsync(id, settings, mutation);
                history.Publish(mutation);
            }
            var afterBytes = await CountAsync(connection, id);
            var afterFile = await CheckpointSizeAsync(connection, root);
            var persisted = await catalog.LoadEditHistoryAsync(id);
            Assert.Equal(64, persisted.Position);
            Assert.Equal("Original", persisted.Entries[0].Label);
            return new(afterBytes - beforeBytes, persisted.Entries.Count, beforeFile, afterFile);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<long> CountAsync(SqliteConnection connection, long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(length(CAST(label AS BLOB)) +
                length(CAST(settings_json AS BLOB))), 0)
            FROM edit_history WHERE image_id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> CheckpointSizeAsync(SqliteConnection connection, string root)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        return new FileInfo(Path.Combine(root, "catalog.db")).Length;
    }
}


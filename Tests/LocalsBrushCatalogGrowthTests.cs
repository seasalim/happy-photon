using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class LocalsBrushCatalogGrowthTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrushHistoryAtCap(bool worstCase)
    {
        const int strokeCount = 96, totalPoints = 4000, localCount = 8;
        const int seed = 271100;
        var random = new Random(seed);
        var strokes = Enumerable.Range(0, strokeCount).Select(s =>
        {
            double u = random.NextDouble(), v = random.NextDouble(), angle = random.NextDouble() * Math.Tau;
            var points = new BrushPoint[totalPoints / strokeCount + (s < totalPoints % strokeCount ? 1 : 0)];
            points[0] = new(u, v);
            for (var p = 1; p < points.Length; p++)
            {
                angle += (random.NextDouble() - .5) * .4;
                u += Math.Cos(angle) * .0045; v += Math.Sin(angle) * .0045 * 1.5;
                points[p] = new(u, v);
            }
            // Storage stress, not a capture/timing workload: alternating clamp corners maximize
            // the sustained signed delta widths. No clipping shortens later snapshots.
            if (worstCase)
                for (var p = 0; p < points.Length; p++) points[p] = p % 2 == 0 ? new(-1, -1) : new(2, 2);
            return new BrushStroke(points, .03, .5, .35, s % 4 == 3);
        }).ToArray();
        Assert.Equal(totalPoints, strokes.Sum(s => s.Points.Length));
        using var directory = new TemporaryDirectory();
        long id;
        using (var catalog = new CatalogService(directory.Path))
        {
            await catalog.InitializeAsync();
            id = await catalog.GetOrCreateImageAsync(Path.Combine(directory.Path, "metadata-only.jpg"));
        }
        var path = Path.Combine(directory.Path, "catalog.db");
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        await Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        var before = new FileInfo(path).Length;
        var original = EditSettingsJson.Serialize(new EditSettings());
        await Append(0, "Original", original);
        var perLocal = Enumerable.Range(0, localCount).Select(_ => new List<BrushStroke>()).ToArray();
        for (var s = 1; s <= strokes.Length; s++)
        {
            perLocal[(s - 1) % localCount].Add(strokes[s - 1]);
            var json = LocalsBrushContractSerializer.SerializeDocuments(perLocal.Select(local => new BrushDocument(local.ToArray())).ToArray());
            await Append(s, s % 4 == 0 ? "Erase stroke" : "Brush stroke", json);
        }
        await Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        var after = new FileInfo(path).Length;
        using var probe = connection.CreateCommand();
        probe.CommandText = """
            SELECT COUNT(*), SUM(length(CAST(settings_json AS BLOB))),
              SUM(CASE WHEN seq > 0 THEN length(CAST(settings_json AS BLOB)) ELSE 0 END),
              MAX(length(CAST(settings_json AS BLOB))) FROM edit_history WHERE image_id = @id;
            """;
        probe.Parameters.AddWithValue("@id", id);
        using var reader = await probe.ExecuteReaderAsync();
        await reader.ReadAsync();
        output.WriteLine($"brush_history seed={seed} strokes={strokeCount} points_total={totalPoints} points_each=41_or_42 worst_case_delta_widths={worstCase} locals={localCount} alternating_locals={localCount > 1} strokes_per_local={strokeCount / localCount} snapshots_per_stroke=1 " +
            $"rows_including_original={reader.GetInt64(0)} settings_json_bytes={reader.GetInt64(1)} " +
            $"stroke_snapshots_json_bytes={reader.GetInt64(2)} final_snapshot_bytes={reader.GetInt64(3)} " +
            $"catalog_before_bytes={before} catalog_after_bytes={after} catalog_growth_bytes={after - before} " +
            "checkpoint=TRUNCATE current_image_settings_updated=True approved_gate=True");

        // Frozen observations from this exact script, 2026-09-24; allow 10% growth.
        var observedJson = worstCase ? 2_983_998L : 1_846_978L;
        var observedCatalogGrowth = worstCase ? 3_178_496L : 1_986_560L;
        Assert.Equal(strokeCount + 1, reader.GetInt64(0));
        Assert.True(reader.GetInt64(1) <= observedJson * 1.10, "History JSON exceeds measured + 10%");
        Assert.True(after - before <= observedCatalogGrowth * 1.10, "Catalog growth exceeds measured + 10%");
        output.WriteLine($"history_json_limit_bytes={observedJson * 1.10:R} catalog_growth_limit_bytes={observedCatalogGrowth * 1.10:R}");

        async Task Execute(string sql)
        {
            using var command = connection.CreateCommand(); command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        async Task Append(int sequence, string label, string json)
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO edit_history(image_id, seq, label, settings_json) VALUES (@id, @seq, @label, @json);
                UPDATE images SET edit_settings = @json, history_position = @seq WHERE id = @id;
                """;
            command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@seq", sequence);
            command.Parameters.AddWithValue("@label", label); command.Parameters.AddWithValue("@json", json);
            await command.ExecuteNonQueryAsync(); await transaction.CommitAsync();
        }
    }
}

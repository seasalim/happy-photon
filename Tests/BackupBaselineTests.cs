using System.Diagnostics;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BackupBaselineCollection
{
    public const string Name = "Backup baseline controls";
}

[Collection(BackupBaselineCollection.Name)]
public sealed class BackupBaselineTests(BackupCatalogFixtures fixtures, ITestOutputHelper output)
{
    [Fact]
    public async Task EverydayFixture_HasPinnedShapeAndSize()
    {
        var fixture = await fixtures.Everyday;
        Assert.InRange(fixture.SizeMiB, 1.5, 4);
        AssertShape(fixture);
        using var connection = Open(fixture);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT edit_settings FROM images WHERE history_position >= 0;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var settings = EditSettingsJson.Deserialize(reader.GetString(0), out var clamped);
            Assert.False(clamped);
            Assert.Equal(4, settings.Version);
            Assert.NotNull(settings.Crop);
            Assert.False(settings.Curve.IsIdentity());
            Assert.NotNull(settings.Mixer);
            Assert.InRange(settings.Locals!.Count, 1, 2);
        }
    }

    [Fact]
    public async Task CopyControl_ReportsPinnedSizesAndFiveSamples()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("HAPPY_PHOTON_PERF") != "1",
            "Set HAPPY_PHOTON_PERF=1 to measure BACKUP-WP1 baseline controls.");
        // Run only under the workflow's exclusive host 'measure' lock. The external
        // test invocation must have a 120 s process-tree timeout (including setup).
        var archive = await fixtures.Archive;
        var everyday = await fixtures.Everyday;
        ReportSize("archive", archive);
        ReportSize("everyday", everyday);
        Assert.InRange(archive.SizeMiB, 160, 260);
        Assert.InRange(everyday.SizeMiB, 1.5, 4);
        AssertShape(archive);
        AssertShape(everyday);

        var samples = new double[5];
        var target = Path.Combine(archive.Root, "copy-control.db");
        try
        {
            for (var run = 0; run < samples.Length; run++)
            {
                var timer = Stopwatch.StartNew();
                using (var source = new FileStream(archive.DatabasePath, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan))
                using (var destination = new FileStream(target, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
                {
                    source.CopyTo(destination, 1024 * 1024);
                    destination.Flush(flushToDisk: true);
                }
                timer.Stop();
                samples[run] = timer.Elapsed.TotalMilliseconds;
                output.WriteLine($"copy_sample_{run + 1}_ms={samples[run]:F3}");
                Assert.Equal(archive.SizeBytes, new FileInfo(target).Length);
                File.Delete(target); // Deletion is outside the measured interval.
            }
        }
        finally
        {
            File.Delete(target);
        }
        var median = samples.Order().ElementAt(2);
        output.WriteLine($"copy_median_ms={median:F3} samples=5 warmups=0 " +
            "buffer_bytes=1048576 flush_to_disk=true cache=uncontrolled " +
            $"temp_volume={Path.GetPathRoot(archive.Root)}");
        Assert.InRange(median, 100, 1000);
    }

    private void ReportSize(string name, BackupCatalogFixture fixture) =>
        output.WriteLine($"{name}_bytes={fixture.SizeBytes} {name}_mib={fixture.SizeMiB:F6} " +
            $"seed={BackupCatalogFixtures.Seed} images={fixture.ImageRows} " +
            $"edited={fixture.EditedRows} history={fixture.HistoryRows}");

    private static SqliteConnection Open(BackupCatalogFixture fixture)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fixture.DatabasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void AssertShape(BackupCatalogFixture fixture)
    {
        using var connection = Open(fixture);
        using var command = connection.CreateCommand();
        long Scalar(string sql)
        {
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar());
        }
        Assert.Equal(CatalogMigrations.CurrentVersion,
            Scalar("SELECT value FROM app_settings WHERE key = 'schema_version';"));
        Assert.Equal(fixture.ImageRows, Scalar("SELECT count(*) FROM images;"));
        Assert.Equal(fixture.ImageRows, Scalar("SELECT count(*) FROM image_assessments;"));
        Assert.Equal(fixture.EditedRows,
            Scalar("SELECT count(*) FROM images WHERE history_position >= 0;"));
        Assert.Equal(fixture.HistoryRows, Scalar("SELECT count(*) FROM edit_history;"));
        Assert.Equal(0, Scalar("SELECT count(*) FROM images WHERE version <> 1 OR edit_version <> 4;"));
        Assert.Equal(0, Scalar("""
            SELECT count(*) FROM images i WHERE history_position >= 0 AND
                (SELECT settings_json FROM edit_history h
                 WHERE h.image_id = i.id AND h.seq = i.history_position) <> i.edit_settings;
            """));
    }
}

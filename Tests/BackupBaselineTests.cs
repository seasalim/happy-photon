using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BackupBaselineCollection
{
    public const string Name = "Backup baseline controls";
}

public sealed class BackupBaselineTests(BackupCatalogFixtures fixtures)
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

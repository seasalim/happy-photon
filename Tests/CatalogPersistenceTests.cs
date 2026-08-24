using HappyPhoton.Models;
using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogPersistenceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonCatalogPersistence_{Guid.NewGuid():N}");

    [Fact]
    public async Task NewImage_StartsWithCurrentEditDocument()
    {
        var (path, id) = await CreateImageAsync("new.jpg");

        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();
        var state = (await service.LoadImageStatesAsync([path]))[path];
        var row = await ReadEditRowAsync(id);

        Assert.Equal(EditSettings.CurrentVersion, state.EditSettings.Version);
        Assert.False(state.EditSettings.HasEdits);
        Assert.Equal(EditSettings.CurrentVersion, row.Version);
        Assert.NotNull(row.Document);
        Assert.Equal(
            EditSettings.CurrentVersion,
            EditSettingsJson.Deserialize(row.Document!, out _).Version);
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData("{not-json", 2)]
    [InlineData("{}", 2)]
    [InlineData("{}", 1)]
    [InlineData("{}", 99)]
    public async Task InvalidRow_ReturnsNeutralSettingsWithoutFailingFolderBatch(
        string? document,
        int editVersion)
    {
        var badPath = Path.Combine(_tempDirectory, "bad.jpg");
        var goodPath = Path.Combine(_tempDirectory, "good.jpg");
        var goodDocument = EditSettingsJson.Serialize(
            new EditSettings { Exposure = 1.25 });
        await CreateCompatibleCatalogAsync(
            badPath,
            document,
            editVersion,
            goodPath,
            goodDocument);

        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();
        var states = await service.LoadImageStatesAsync([badPath, goodPath]);

        Assert.False(states[badPath].EditSettings.HasEdits);
        Assert.Equal(EditSettings.CurrentVersion, states[badPath].EditSettings.Version);
        Assert.Equal(1.25, states[goodPath].EditSettings.Exposure);
        var persisted = await ReadEditRowAsync(badPath);
        Assert.Equal(editVersion, persisted.Version);
        Assert.Equal(document, persisted.Document);
    }

    [Fact]
    public async Task OutOfRangeRow_ClampsInMemoryWithoutRewritingDocument()
    {
        var (path, id) = await CreateImageAsync("clamped.jpg");
        var document = EditSettingsJson.Serialize(new EditSettings { Exposure = 1 })
            .Replace("\"exposure\":1", "\"exposure\":9", StringComparison.Ordinal);
        await UpdateEditRowAsync(id, document, EditSettings.CurrentVersion);

        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();
        var state = (await service.LoadImageStatesAsync([path]))[path];

        Assert.Equal(3, state.EditSettings.Exposure);
        Assert.Equal(document, (await ReadEditRowAsync(id)).Document);
    }

    [Fact]
    public async Task LegacyRowMaterializesAllOffBaselineWithoutRewritingDocument()
    {
        var (path, id) = await CreateImageAsync("legacy.dng");
        var current = EditSettingsJson.Serialize(new EditSettings());
        var legacy = current
            .Replace("\"version\":3", "\"version\":2", StringComparison.Ordinal)
            .Replace(",\"lens\":{\"distortion\":true,\"chromaticAberration\":true," +
                "\"vignetting\":false,\"baseline\":\"standard\"}", "",
                StringComparison.Ordinal);
        await UpdateEditRowAsync(id, legacy, 2);

        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();
        var settings = (await service.LoadImageStatesAsync([path]))[path].EditSettings;

        Assert.Equal(LensBaseline.Legacy, settings.Lens.Baseline);
        Assert.False(settings.Lens.Distortion);
        Assert.False(settings.Lens.ChromaticAberration);
        Assert.False(settings.Lens.Vignetting);
        Assert.False(settings.HasEdits);
        Assert.Equal(new PersistedEditRow(legacy, 2), await ReadEditRowAsync(id));
    }

    [Fact]
    public async Task Save_WritesCompleteCurrentDocument()
    {
        var (_, id) = await CreateImageAsync("save.jpg");
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();
        var settings = new EditSettings
        {
            Exposure = 2.5,
            Wb = new WhiteBalanceSettings
            {
                Mode = WbMode.Custom,
                Kelvin = 7200,
                Tint = -10
            },
            Brightness = 40,
            Contrast = 50,
            Saturation = 60,
            Vibrance = 70,
            Shadows = 80,
            Highlights = 90,
            Rotation = 180,
            HorizonRotation = 4,
            Geometry = new GeometrySettings
            {
                Vertical = -18,
                Horizontal = 27,
                Aspect = -36,
                Distortion = 45
            },
            AppliedPresetId = "user_new",
            RawProfile = new RawProfileSelection
            {
                Source = RawProfileSource.UserFile,
                Location = Path.Combine(_tempDirectory, "camera.dcp"),
                ContentHash = new string('a', 64)
            }
        };

        await service.SaveEditSettingsAsync(id, settings);

        var row = await ReadEditRowAsync(id);
        Assert.Equal(EditSettings.CurrentVersion, row.Version);
        var saved = EditSettingsJson.Deserialize(row.Document!, out var clamped);
        Assert.False(clamped);
        Assert.Equal(2.5, saved.Exposure);
        Assert.Equal(WbMode.Custom, saved.Wb.Mode);
        Assert.Equal(7200, saved.Wb.Kelvin);
        Assert.Equal(-10, saved.Wb.Tint);
        Assert.Equal(180, saved.Rotation);
        Assert.Equal(-18, saved.Geometry?.Vertical);
        Assert.Equal(27, saved.Geometry?.Horizontal);
        Assert.Equal(-36, saved.Geometry?.Aspect);
        Assert.Equal(45, saved.Geometry?.Distortion);
        Assert.Equal("user_new", saved.AppliedPresetId);
        Assert.Equal(settings.RawProfile.Location, saved.RawProfile?.Location);
        Assert.Equal(settings.RawProfile.ContentHash,
            saved.RawProfile?.ContentHash);
    }

    [Fact]
    public async Task Save_GeometryDoesNotTouchExistingSidecar()
    {
        var (path, id) = await CreateImageAsync("sidecar.jpg");
        var sidecar = path + ".xmp";
        var bytes = "<x:xmpmeta known='1' unknown='preserve'/>"u8.ToArray();
        await File.WriteAllBytesAsync(sidecar, bytes);
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();

        await service.SaveEditSettingsAsync(id, new EditSettings
        {
            Geometry = new GeometrySettings { Distortion = 50 }
        });

        Assert.Equal(bytes, await File.ReadAllBytesAsync(sidecar));
    }

    private async Task<(string Path, long Id)> CreateImageAsync(string fileName)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();
        return (path, await service.GetOrCreateImageAsync(path));
    }

    private async Task CreateCompatibleCatalogAsync(
        string badPath,
        string? badDocument,
        int badVersion,
        string goodPath,
        string goodDocument)
    {
        Directory.CreateDirectory(_tempDirectory);
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE images (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL COLLATE NOCASE UNIQUE,
                file_name TEXT NOT NULL,
                edit_settings TEXT,
                edit_version INTEGER NOT NULL,
                flag_state INTEGER DEFAULT 0,
                rating INTEGER DEFAULT 0,
                updated_utc TEXT
            );
            CREATE TABLE app_settings (key TEXT PRIMARY KEY, value TEXT);
            INSERT INTO images (
                file_path, file_name, edit_settings, edit_version)
            VALUES (@badPath, 'bad.jpg', @badDocument, @badVersion);
            INSERT INTO images (
                file_path, file_name, edit_settings, edit_version)
            VALUES (@goodPath, 'good.jpg', @goodDocument, @goodVersion);
            """;
        command.Parameters.AddWithValue("@badPath", badPath);
        command.Parameters.AddWithValue(
            "@badDocument",
            badDocument is null ? DBNull.Value : badDocument);
        command.Parameters.AddWithValue("@badVersion", badVersion);
        command.Parameters.AddWithValue("@goodPath", goodPath);
        command.Parameters.AddWithValue("@goodDocument", goodDocument);
        command.Parameters.AddWithValue("@goodVersion", EditSettings.CurrentVersion);
        await command.ExecuteNonQueryAsync();
    }

    private async Task UpdateEditRowAsync(long id, string document, int version)
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE images SET edit_settings = @document, edit_version = @version
            WHERE id = @id
            """;
        command.Parameters.AddWithValue("@document", document);
        command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<PersistedEditRow> ReadEditRowAsync(long id)
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT edit_settings, edit_version FROM images WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", id);
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PersistedEditRow(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetInt32(1));
    }

    private async Task<PersistedEditRow> ReadEditRowAsync(string path)
    {
        await using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT edit_settings, edit_version FROM images WHERE file_path = @path
            """;
        command.Parameters.AddWithValue("@path", path);
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new PersistedEditRow(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetInt32(1));
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var path = Path.Combine(_tempDirectory, "catalog.db");
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync();
        return connection;
    }

    private sealed record PersistedEditRow(string? Document, int Version);

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}

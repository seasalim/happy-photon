using System.Text.Json;
using Microsoft.Data.Sqlite;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// SQLite-based catalog service for centralized storage of edit settings,
/// thumbnails, previews, and application settings.
/// </summary>
public partial class CatalogService : ICatalogService, IDisposable
{
    private const string CatalogFolderName = "Happy Photon Catalog";
    private const string DatabaseFileName = "catalog.db";

    private readonly string _catalogPath;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private SqliteConnection? _connection;
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public string CatalogPath => _catalogPath;

    public CatalogService() : this(GetDefaultCatalogPath())
    {
    }

    public CatalogService(string catalogPath)
    {
        _catalogPath = catalogPath;
        _databasePath = Path.Combine(_catalogPath, DatabaseFileName);
    }

    private static string GetDefaultCatalogPath()
    {
        var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrEmpty(picturesPath))
        {
            picturesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
        }
        return Path.Combine(picturesPath, CatalogFolderName);
    }

    public async Task InitializeAsync()
    {
        await _connectionGate.WaitAsync();
        try
        {
            if (_initialized) return;

            Directory.CreateDirectory(_catalogPath);
            Directory.CreateDirectory(Path.Combine(_catalogPath, "assets", "thumbs"));
            Directory.CreateDirectory(Path.Combine(_catalogPath, "assets", "previews"));
            var temporaryAssetsPath = Path.Combine(_catalogPath, "assets", "tmp");
            Directory.CreateDirectory(temporaryAssetsPath);
            foreach (var orphan in Directory.EnumerateFiles(temporaryAssetsPath))
            {
                try
                {
                    File.Delete(orphan);
                }
                catch
                {
                }
            }

            _connection = new SqliteConnection($"Data Source={_databasePath}");
            try
            {
                await _connection.OpenAsync();
                await CatalogSchema.InitializeAsync(_connection);
                _initialized = true;
            }
            catch
            {
                _connection.Dispose();
                _connection = null;
                throw;
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("CatalogService not initialized. Call InitializeAsync() first.");
    }

    public async Task<long> GetOrCreateImageAsync(string filePath)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync();
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO images (file_path, file_name, updated_utc)
                VALUES (@path, @name, @updated)
                ON CONFLICT(file_path) DO UPDATE SET file_name = excluded.file_name
                RETURNING id;
            ";
            cmd.Parameters.AddWithValue("@path", filePath);
            cmd.Parameters.AddWithValue("@name", Path.GetFileName(filePath));
            cmd.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));

            var result = await cmd.ExecuteScalarAsync();
            return (long)result!;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<long?> GetImageIdAsync(string filePath)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync();
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT id FROM images WHERE file_path COLLATE NOCASE = @path;";
            cmd.Parameters.AddWithValue("@path", filePath);

            var result = await cmd.ExecuteScalarAsync();
            return result != null ? (long)result : null;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, CatalogImageState>> LoadImageStatesAsync(
        IReadOnlyCollection<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        var states = new Dictionary<string, CatalogImageState>(StringComparer.OrdinalIgnoreCase);
        const int batchSize = 500;
        var paths = filePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        for (var offset = 0; offset < paths.Length; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _connectionGate.WaitAsync(cancellationToken);
            try
            {
                var count = Math.Min(batchSize, paths.Length - offset);
                using var cmd = _connection!.CreateCommand();
                var parameterNames = new string[count];
                for (var index = 0; index < count; index++)
                {
                    var name = $"@path{index}";
                    parameterNames[index] = name;
                    cmd.Parameters.AddWithValue(name, paths[offset + index]);
                }

                cmd.CommandText = $@"
                SELECT file_path, id, exposure, temperature, brightness, contrast,
                       saturation, vibrance, shadows, highlights, rotation,
                       horizon_rotation, crop_data, curve_data, applied_preset_id,
                       flag_state, rating
                FROM images
                WHERE file_path COLLATE NOCASE IN ({string.Join(", ", parameterNames)});
            ";

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var path = reader.GetString(0);
                    var catalogId = reader.GetInt64(1);
                    var settings = ReadEditSettings(reader, 2);
                    var flagValue = reader.IsDBNull(15) ? 0 : reader.GetInt32(15);
                    var flag = Enum.IsDefined(typeof(ImageFlag), flagValue)
                        ? (ImageFlag)flagValue
                        : ImageFlag.Unflagged;
                    var rating = reader.IsDBNull(16) ? 0 : Math.Clamp(reader.GetInt32(16), 0, 5);
                    states[path] = new CatalogImageState(catalogId, settings, flag, rating);
                }
            }
            finally
            {
                _connectionGate.Release();
            }
        }

        return states;
    }

    private static EditSettings ReadEditSettings(SqliteDataReader reader, int offset)
    {
        var settings = new EditSettings
        {
            Exposure = reader.IsDBNull(offset) ? 0.0 : reader.GetDouble(offset),
            Temperature = reader.IsDBNull(offset + 1) ? 0 : reader.GetInt32(offset + 1),
            Brightness = reader.IsDBNull(offset + 2) ? 0 : reader.GetInt32(offset + 2),
            Contrast = reader.IsDBNull(offset + 3) ? 0 : reader.GetInt32(offset + 3),
            Saturation = reader.IsDBNull(offset + 4) ? 0 : reader.GetInt32(offset + 4),
            Vibrance = reader.IsDBNull(offset + 5) ? 0 : reader.GetInt32(offset + 5),
            Shadows = reader.IsDBNull(offset + 6) ? 0 : reader.GetInt32(offset + 6),
            Highlights = reader.IsDBNull(offset + 7) ? 0 : reader.GetInt32(offset + 7),
            Rotation = reader.IsDBNull(offset + 8) ? 0 : reader.GetInt32(offset + 8),
            HorizonRotation = reader.IsDBNull(offset + 9) ? 0.0 : reader.GetDouble(offset + 9),
            AppliedPresetId = reader.IsDBNull(offset + 12) ? null : reader.GetString(offset + 12)
        };

        // Deserialize crop data
        if (!reader.IsDBNull(offset + 10))
        {
            var cropJson = reader.GetString(offset + 10);
            try
            {
                var crop = JsonSerializer.Deserialize<CropRegion>(cropJson);
                if (crop != null)
                {
                    settings.Crop = crop;
                }
            }
            catch
            {
                // Invalid crop data, use default
            }
        }

        // Deserialize curve data
        if (!reader.IsDBNull(offset + 11))
        {
            var curveJson = reader.GetString(offset + 11);
            try
            {
                var curve = JsonSerializer.Deserialize<CurveData>(curveJson);
                if (curve != null)
                {
                    curve.BuildLookupTable();
                    settings.Curve = curve;
                }
            }
            catch
            {
                // Invalid curve data, use default
            }
        }

        return settings;
    }

    public string GetThumbnailPath(long catalogId)
    {
        var prefix = (catalogId % 256).ToString("x2");
        return Path.Combine(_catalogPath, "assets", "thumbs", prefix, $"{catalogId}.jpg");
    }

    public string GetPreviewPath(long catalogId)
    {
        var prefix = (catalogId % 256).ToString("x2");
        return Path.Combine(_catalogPath, "assets", "previews", prefix, $"{catalogId}.jpg");
    }

    public void Dispose()
    {
        if (_connection != null)
        {
            _connection.Close();
            SqliteConnection.ClearPool(_connection);
            _connection.Dispose();
        }
        _connection = null;
        _initialized = false;
    }
}

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// SQLite-based catalog service for centralized storage of edit settings,
/// thumbnails, previews, and application settings.
/// </summary>
public partial class CatalogService : IDisposable
{
    private const string CatalogFolderName = "Happy Photon Catalog";
    private const string DatabaseFileName = "catalog.db";
    private static readonly string DefaultEditSettingsJson =
        EditSettingsJson.Serialize(new EditSettings());

    private readonly string _catalogPath;
    private readonly string _databasePath;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly HashSet<long> _editSettingsWarnings = new();
    private SqliteConnection? _connection;
    private bool _initialized;

    /// <summary>Gets the catalog root directory path.</summary>
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

    /// <summary>Initializes the catalog database and directory structure.</summary>
    public async Task InitializeAsync()
    {
        await _connectionGate.WaitAsync();
        try
        {
            if (_initialized) return;

            Directory.CreateDirectory(_catalogPath);
            Directory.CreateDirectory(Path.Combine(_catalogPath, "assets", "thumbs"));
            Directory.CreateDirectory(Path.Combine(_catalogPath, "assets", "previews"));
            Directory.CreateDirectory(Path.Combine(_catalogPath, "assets", "rendered-thumbs"));
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
                _connection.Close();
                SqliteConnection.ClearPool(_connection);
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

    /// <summary>Gets or creates an image record by file path.</summary>
    public async Task<long> GetOrCreateImageAsync(string filePath)
    {
        EnsureInitialized();
        await _connectionGate.WaitAsync();
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO images (
                    file_path, file_name, edit_settings, edit_version, updated_utc)
                VALUES (@path, @name, @editSettings, @editVersion, @updated)
                ON CONFLICT(file_path) DO UPDATE SET file_name = excluded.file_name
                RETURNING id;
            ";
            cmd.Parameters.AddWithValue("@path", filePath);
            cmd.Parameters.AddWithValue("@name", Path.GetFileName(filePath));
            cmd.Parameters.AddWithValue("@editSettings", DefaultEditSettingsJson);
            cmd.Parameters.AddWithValue("@editVersion", EditSettings.CurrentVersion);
            cmd.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));

            var result = await cmd.ExecuteScalarAsync();
            return (long)result!;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    /// <summary>Loads catalog state for many paths in a small number of queries.</summary>
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
                SELECT file_path, id, edit_settings, edit_version, flag_state, rating,
                       color_label
                FROM images
                WHERE file_path COLLATE NOCASE IN ({string.Join(", ", parameterNames)});
            ";

                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var path = reader.GetString(0);
                    var catalogId = reader.GetInt64(1);
                    var settings = ReadEditSettings(reader, 2, catalogId, path);
                    var flag = ReadEnumColumn(reader, 4, ImageFlag.Unflagged);
                    var rating = reader.IsDBNull(5)
                        ? 0
                        : (int)Math.Clamp(reader.GetInt64(5), 0, 5);
                    var colorLabel = ReadEnumColumn(reader, 6, ColorLabel.None);
                    states[path] = new CatalogImageState(
                        catalogId, settings, flag, rating, colorLabel);
                }
            }
            finally
            {
                _connectionGate.Release();
            }
        }

        return states;
    }

    /// <summary>
    /// Reads an enum-backed column as a 64-bit value so that an out-of-range integer
    /// degrades to the fallback instead of overflowing, and never rewrites the row.
    /// </summary>
    private static TEnum ReadEnumColumn<TEnum>(
        SqliteDataReader reader,
        int ordinal,
        TEnum fallback)
        where TEnum : struct, Enum
    {
        if (reader.IsDBNull(ordinal)) return fallback;

        var value = reader.GetInt64(ordinal);
        return value is >= 0 and <= int.MaxValue &&
               Enum.IsDefined(typeof(TEnum), (int)value)
            ? (TEnum)Enum.ToObject(typeof(TEnum), (int)value)
            : fallback;
    }

    private EditSettings ReadEditSettings(
        SqliteDataReader reader,
        int offset,
        long catalogId,
        string filePath)
    {
        var editVersion = reader.IsDBNull(offset + 1)
            ? 0
            : reader.GetInt32(offset + 1);
        if (editVersion != EditSettings.CurrentVersion)
        {
            LogEditSettingsWarningOnce(
                catalogId,
                $"Unsupported edit settings version {editVersion} for '{filePath}'.");
            return new EditSettings();
        }

        if (reader.IsDBNull(offset))
        {
            LogEditSettingsWarningOnce(
                catalogId,
                $"Missing v2 edit settings for '{filePath}'.");
            return new EditSettings();
        }

        try
        {
            var settings = EditSettingsJson.Deserialize(
                reader.GetString(offset),
                out var wasClamped);
            if (wasClamped)
            {
                LogEditSettingsWarningOnce(
                    catalogId,
                    $"Clamped out-of-range v2 edit settings for '{filePath}'.");
            }
            return settings;
        }
        catch (JsonException exception)
        {
            LogEditSettingsWarningOnce(
                catalogId,
                $"Invalid v2 edit settings for '{filePath}': {exception.Message}");
            return new EditSettings();
        }
    }

    private void LogEditSettingsWarningOnce(long catalogId, string message)
    {
        if (_editSettingsWarnings.Add(catalogId))
        {
            Debug.WriteLine($"[HappyPhoton] {message}");
        }
    }

    /// <summary>Gets the cached thumbnail path for an image.</summary>
    public string GetThumbnailPath(long catalogId)
    {
        var prefix = (catalogId % 256).ToString("x2");
        return Path.Combine(_catalogPath, "assets", "thumbs", prefix, $"{catalogId}.jpg");
    }

    /// <summary>Gets the cached preview path for an image.</summary>
    public string GetPreviewPath(long catalogId)
    {
        var prefix = (catalogId % 256).ToString("x2");
        return Path.Combine(_catalogPath, "assets", "previews", prefix, $"{catalogId}.jpg");
    }

    /// <summary>Gets the accurate rendered RAW thumbnail path for an image.</summary>
    public string GetRenderedThumbnailPath(long catalogId)
    {
        var prefix = (catalogId % 256).ToString("x2");
        return Path.Combine(
            _catalogPath,
            "assets",
            "rendered-thumbs",
            prefix,
            $"{catalogId}.jpg");
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

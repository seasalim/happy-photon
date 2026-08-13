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

    private string? _catalogPath;
    private string? _cachePath;
    private string? _databasePath;
    private AppDataLocations? _locations;
    private CatalogIdentity? _identity;
    private long _lastStampedMaxImageId;
    private readonly bool _explicitPath;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly HashSet<long> _editSettingsWarnings = new();
    private SqliteConnection? _connection;
    private bool _initialized;

    /// <summary>Gets the catalog root directory path.</summary>
    public string CatalogPath => _catalogPath ?? GetDefaultCatalogPath();
    public string CachePath => _cachePath ?? CatalogPath;
    public string TemporaryAssetsPath =>
        Path.Combine(CachePath, "assets", "tmp");
    internal bool HasExplicitPath => _explicitPath;

    public CatalogService()
    {
    }

    public CatalogService(string catalogPath)
    {
        var normalized = Path.GetFullPath(catalogPath);
        _catalogPath = normalized;
        _cachePath = normalized;
        _databasePath = Path.Combine(normalized, DatabaseFileName);
        _locations = new AppDataLocations(
            normalized,
            normalized,
            AppDataLocationOrigin.Persisted,
            AppDataLocationOrigin.Persisted,
            AppDataLocationTopology.LegacyCoLocated);
        _explicitPath = true;
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
        var locations = _locations ?? new AppDataLocations(
            GetDefaultCatalogPath(),
            GetDefaultCatalogPath(),
            AppDataLocationOrigin.AdoptedDefault,
            AppDataLocationOrigin.AdoptedDefault,
            AppDataLocationTopology.LegacyCoLocated);
        await InitializeAsync(locations);
    }

    public async Task InitializeAsync(AppDataLocations locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        await _connectionGate.WaitAsync();
        try
        {
            if (_initialized) return;

            if (_explicitPath)
            {
                AppDataRootOwnership.Claim(locations.CatalogRoot);
            }
            else if (!Directory.Exists(locations.CatalogRoot))
            {
                throw new DirectoryNotFoundException(
                    $"The catalog location '{locations.CatalogRoot}' is missing.");
            }
            else if (File.Exists(Path.Combine(
                         locations.CatalogRoot, AppDataRootOwnership.MarkerFileName)) ||
                     File.Exists(locations.DatabasePath))
            {
                // Opening is non-destructive and the pointer is the record of the
                // claim: a marker lost to a backup restore or a downgraded-build run
                // is re-written here. The catalog signature gates the re-mark so a
                // hand-edited pointer cannot claim an arbitrary folder, and a marker
                // with foreign contents still refuses. Destructive operations keep
                // their own AssertAppOwned.
                AppDataRootOwnership.Claim(locations.CatalogRoot);
            }
            else
            {
                AppDataRootOwnership.AssertAppOwned(locations.CatalogRoot);
            }
            if (!Directory.Exists(locations.CacheRoot))
            {
                AppDataRootOwnership.ClaimFresh(locations.CacheRoot);
            }
            else if (File.Exists(Path.Combine(
                         locations.CacheRoot, AppDataRootOwnership.MarkerFileName)) ||
                     Directory.Exists(locations.AssetsRoot) ||
                     !Directory.EnumerateFileSystemEntries(locations.CacheRoot).Any())
            {
                AppDataRootOwnership.Claim(locations.CacheRoot);
            }
            else
            {
                AppDataRootOwnership.AssertAppOwned(locations.CacheRoot);
            }

            _locations = locations;
            _catalogPath = locations.CatalogRoot;
            _cachePath = locations.CacheRoot;
            _databasePath = locations.DatabasePath;
            _identity = CatalogCacheStamp.EnsureIdentity(
                locations.CatalogRoot,
                out var identityCreated);
            Directory.CreateDirectory(Path.Combine(locations.AssetsRoot, "thumbs"));
            Directory.CreateDirectory(Path.Combine(locations.AssetsRoot, "previews"));
            Directory.CreateDirectory(Path.Combine(locations.AssetsRoot, "rendered-thumbs"));
            var temporaryAssetsPath = locations.TemporaryAssetsRoot;
            Directory.CreateDirectory(temporaryAssetsPath);
            foreach (var orphan in Directory.EnumerateFiles(temporaryAssetsPath))
            {
                try
                {
                    AppDataRootOwnership.AssertAppOwned(locations.CacheRoot);
                    File.Delete(orphan);
                }
                catch
                {
                }
            }

            LightroomCatalogReader.SweepOrphanedSnapshots();

            _connection = new SqliteConnection($"Data Source={_databasePath}");
            try
            {
                await _connection.OpenAsync();
                await CatalogSchema.InitializeAsync(_connection);
                _lastStampedMaxImageId = await CatalogCacheStamp.CheckAndRefreshAsync(
                    _connection,
                    locations,
                    _identity,
                    identityCreated &&
                    locations.Topology == AppDataLocationTopology.LegacyCoLocated);
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

    public async Task ReopenAsync(AppDataLocations locations)
    {
        await _connectionGate.WaitAsync();
        try
        {
            CloseConnection();
        }
        finally
        {
            _connectionGate.Release();
        }
        await InitializeAsync(locations);
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

            var result = (long)(await cmd.ExecuteScalarAsync())!;
            await RefreshCacheStampAsync(result);
            return result;
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
                SELECT images.file_path, images.id, images.edit_settings,
                       images.edit_version, images.flag_state, images.rating,
                       images.color_label, image_assessments.revision,
                       image_assessments.assessed_utc,
                       image_assessments.pending_axes
                FROM images
                LEFT JOIN image_assessments
                  ON image_assessments.image_id = images.id
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
                    var revision = reader.IsDBNull(7) ? 0 : reader.GetInt64(7);
                    DateTime? assessedUtc = reader.IsDBNull(8)
                        ? null
                        : DateTime.Parse(
                            reader.GetString(8),
                            null,
                            System.Globalization.DateTimeStyles.RoundtripKind);
                    var pendingAxes = reader.IsDBNull(9)
                        ? AssessmentAxes.None
                        : (AssessmentAxes)reader.GetInt32(9);
                    states[path] = new CatalogImageState(
                        catalogId, settings, flag, rating, colorLabel,
                        revision, assessedUtc, pendingAxes);
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
        return Path.Combine(CachePath, "assets", "thumbs", prefix, $"{catalogId}.jpg");
    }

    /// <summary>Gets the cached preview path for an image.</summary>
    public string GetPreviewPath(long catalogId)
    {
        var prefix = (catalogId % 256).ToString("x2");
        return Path.Combine(CachePath, "assets", "previews", prefix, $"{catalogId}.jpg");
    }

    /// <summary>Gets the accurate rendered RAW thumbnail path for an image.</summary>
    public string GetRenderedThumbnailPath(long catalogId)
    {
        var prefix = (catalogId % 256).ToString("x2");
        return Path.Combine(
            CachePath,
            "assets",
            "rendered-thumbs",
            prefix,
            $"{catalogId}.jpg");
    }

    public void Dispose()
    {
        CloseConnection();
    }

    private async Task RefreshCacheStampAsync()
    {
        var maxImageId = await CatalogCacheStamp.ReadMaxImageIdAsync(_connection!);
        await RefreshCacheStampAsync(maxImageId);
    }

    private async Task RefreshCacheStampAsync(long maxImageId)
    {
        if (maxImageId <= _lastStampedMaxImageId) return;
        await CatalogCacheStamp.RefreshAsync(
            _locations!, _identity!, maxImageId);
        _lastStampedMaxImageId = maxImageId;
    }

    private void CloseConnection()
    {
        if (_connection != null)
        {
            _connection.Close();
            SqliteConnection.ClearPool(_connection);
            _connection.Dispose();
        }
        _connection = null;
        _initialized = false;
        _lastStampedMaxImageId = 0;
    }
}

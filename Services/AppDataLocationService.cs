using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

public sealed class AppDataLocationPointerException(
    string pointerPath,
    string message,
    Exception? innerException = null) : IOException(message, innerException)
{
    public string PointerPath { get; } = pointerPath;
}

internal sealed record AppDataPlatformPaths(
    string PicturesRoot,
    string PointerRoot,
    string StandardCatalogRoot,
    string StandardCacheRoot);

public sealed class AppDataLocationService
{
    public const string CatalogEnvironmentVariable = "HAPPY_PHOTON_CATALOG_ROOT";
    public const string CacheEnvironmentVariable = "HAPPY_PHOTON_CACHE_ROOT";
    private const string PointerFileName = "locations.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AppDataPlatformPaths _paths;
    private readonly Func<string, string?> _environment;
    private AppDataLocations? _persisted;

    public AppDataLocationService()
        : this(CreatePlatformPaths(), Environment.GetEnvironmentVariable)
    {
    }

    internal AppDataLocationService(
        AppDataPlatformPaths paths,
        Func<string, string?>? environment = null)
    {
        _paths = paths;
        _environment = environment ?? (_ => null);
    }

    public string PointerPath => Path.Combine(_paths.PointerRoot, PointerFileName);
    internal string PointerRoot => _paths.PointerRoot;
    public string DefaultCatalogRoot => Path.Combine(
        _paths.PicturesRoot, "Happy Photon Catalog");
    public string StandardCatalogRoot => _paths.StandardCatalogRoot;
    public string StandardCacheRoot => _paths.StandardCacheRoot;

    public async Task<AppDataLocations?> ResolveAsync()
    {
        PersistedLocations? pointer = null;
        if (File.Exists(PointerPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(PointerPath);
                pointer = JsonSerializer.Deserialize<PersistedLocations>(json, JsonOptions);
                if (pointer is not { Version: 1 } ||
                    string.IsNullOrWhiteSpace(pointer.CatalogRoot) ||
                    string.IsNullOrWhiteSpace(pointer.CacheRoot))
                {
                    throw new JsonException("The location pointer is incomplete.");
                }
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
                throw new AppDataLocationPointerException(
                    PointerPath,
                    "The storage location pointer is unreadable. Recover it before opening the catalog.",
                    exception);
            }
        }

        AppDataLocations? locations;
        if (pointer == null)
        {
            locations = await AdoptConventionalCatalogAsync();
            if (locations == null) return ResolveFromEnvironmentOnly();
        }
        else
        {
            try
            {
                locations = new AppDataLocations(
                    pointer.CatalogRoot,
                    pointer.CacheRoot,
                    AppDataLocationOrigin.Persisted,
                    AppDataLocationOrigin.Persisted,
                    pointer.LegacyCoLocated
                        ? AppDataLocationTopology.LegacyCoLocated
                        : AppDataLocationTopology.Split);
                Validate(locations);
            }
            catch (ArgumentException exception)
            {
                throw new AppDataLocationPointerException(
                    PointerPath,
                    "The storage location pointer names an invalid location. Recover it before opening the catalog.",
                    exception);
            }
        }
        AppDataRootOwnership.Claim(_paths.PointerRoot);
        _persisted = locations;
        locations = ApplyEnvironment(locations);
        Validate(locations);
        if (locations.CatalogOrigin == AppDataLocationOrigin.Persisted &&
            !Directory.Exists(locations.CatalogRoot))
        {
            throw new AppDataLocationPointerException(
                PointerPath,
                $"The catalog folder at '{locations.CatalogRoot}' can't be found. Reconnect the drive it lives on, or recover to start fresh.");
        }
        HealCacheRoot(locations.CacheRoot);
        return locations;
    }

    public async Task<AppDataLocations> CreateFreshAsync(
        bool useStandardCatalog = false,
        string? catalogRoot = null,
        string? cacheRoot = null)
    {
        var locations = new AppDataLocations(
            catalogRoot ?? (useStandardCatalog ? StandardCatalogRoot : DefaultCatalogRoot),
            cacheRoot ?? StandardCacheRoot,
            AppDataLocationOrigin.Persisted,
            AppDataLocationOrigin.Persisted);
        Validate(locations);
        AppDataRootOwnership.ClaimFresh(locations.CatalogRoot);
        AppDataRootOwnership.ClaimFresh(locations.CacheRoot);
        await PersistAsync(locations);
        _persisted = locations;
        return ApplyEnvironment(locations);
    }

    internal async Task<AppDataLocations> CreateFreshAfterSetAsideAsync()
    {
        if (HasCatalogSignature(DefaultCatalogRoot))
        {
            throw new InvalidOperationException(
                "Set-aside replacement refused because the default location already contains a Happy Photon catalog. Choose a different catalog location before starting fresh.");
        }
        return await CreateFreshAsync();
    }

    public async Task<AppDataLocations?> QuarantineCorruptPointerAsync()
    {
        if (File.Exists(PointerPath))
        {
            var quarantine = $"{PointerPath}.{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.corrupt";
            AppDataRootOwnership.Claim(_paths.PointerRoot);
            AppDataRootOwnership.AssertAppOwned(_paths.PointerRoot);
            File.Move(PointerPath, quarantine);
        }
        return await ResolveAsync();
    }

    public Task PersistAsync(AppDataLocations locations)
    {
        var catalogRoot = locations.IsCatalogEnvironmentManaged
            ? _persisted?.CatalogRoot ?? throw new InvalidOperationException(
                "An environment-supplied catalog path cannot be persisted.")
            : locations.CatalogRoot;
        var cacheRoot = locations.IsCacheEnvironmentManaged
            ? _persisted?.CacheRoot ?? throw new InvalidOperationException(
                "An environment-supplied cache path cannot be persisted.")
            : locations.CacheRoot;
        var persisted = new PersistedLocations(
            1,
            catalogRoot,
            cacheRoot,
            locations.Topology == AppDataLocationTopology.LegacyCoLocated);
        var json = JsonSerializer.Serialize(persisted, JsonOptions);
        AppDataRootOwnership.Claim(_paths.PointerRoot);
        AppDataRootOwnership.WriteAtomicOwned(_paths.PointerRoot, PointerPath, json);
        _persisted = new AppDataLocations(
            catalogRoot,
            cacheRoot,
            AppDataLocationOrigin.Persisted,
            AppDataLocationOrigin.Persisted,
            locations.Topology);
        return Task.CompletedTask;
    }

    private async Task<AppDataLocations?> AdoptConventionalCatalogAsync()
    {
        var catalog = DefaultCatalogRoot;
        if (!HasCatalogSignature(catalog)) return null;

        var legacy = Directory.Exists(Path.Combine(catalog, "assets"));
        AppDataRootOwnership.Claim(catalog);
        var cache = legacy ? catalog : StandardCacheRoot;
        if (!legacy) AppDataRootOwnership.ClaimFresh(cache);
        var locations = new AppDataLocations(
            catalog,
            cache,
            AppDataLocationOrigin.AdoptedDefault,
            AppDataLocationOrigin.AdoptedDefault,
            legacy
                ? AppDataLocationTopology.LegacyCoLocated
                : AppDataLocationTopology.Split);
        await PersistAsync(locations);
        return locations;
    }

    // A catalog env override must open even when no pointer or conventional
    // catalog exists; nothing is persisted, so the pointer flow is untouched.
    // A cache-only override still routes through the location gate, which
    // persists defaults while the env cache stays effective and unpersisted.
    private AppDataLocations? ResolveFromEnvironmentOnly()
    {
        var catalog = _environment(CatalogEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(catalog)) return null;
        var cache = _environment(CacheEnvironmentVariable);
        var locations = new AppDataLocations(
            catalog,
            string.IsNullOrWhiteSpace(cache) ? _paths.StandardCacheRoot : cache,
            AppDataLocationOrigin.Environment,
            string.IsNullOrWhiteSpace(cache)
                ? AppDataLocationOrigin.AdoptedDefault
                : AppDataLocationOrigin.Environment);
        Validate(locations);
        AppDataRootOwnership.Claim(locations.CatalogRoot);
        HealCacheRoot(locations.CacheRoot);
        return locations;
    }

    private AppDataLocations ApplyEnvironment(AppDataLocations locations)
    {
        var catalog = _environment(CatalogEnvironmentVariable);
        var cache = _environment(CacheEnvironmentVariable);
        return new AppDataLocations(
            string.IsNullOrWhiteSpace(catalog) ? locations.CatalogRoot : catalog,
            string.IsNullOrWhiteSpace(cache) ? locations.CacheRoot : cache,
            string.IsNullOrWhiteSpace(catalog)
                ? locations.CatalogOrigin
                : AppDataLocationOrigin.Environment,
            string.IsNullOrWhiteSpace(cache)
                ? locations.CacheOrigin
                : AppDataLocationOrigin.Environment,
            string.IsNullOrWhiteSpace(catalog) && string.IsNullOrWhiteSpace(cache)
                ? locations.Topology
                : AppDataLocationTopology.Split);
    }

    private static void Validate(AppDataLocations locations)
    {
        if (locations.Topology != AppDataLocationTopology.LegacyCoLocated)
        {
            AppDataRootOwnership.ValidateProposedRoots(
                locations.CatalogRoot,
                locations.CacheRoot);
        }
    }

    private static void HealCacheRoot(string cacheRoot)
    {
        if (!Directory.Exists(cacheRoot)) AppDataRootOwnership.ClaimFresh(cacheRoot);
    }

    internal static bool HasCatalogSignature(string catalogRoot)
    {
        var database = Path.Combine(catalogRoot, "catalog.db");
        if (!File.Exists(database)) return false;
        try
        {
            using var connection = new SqliteConnection(
                $"Data Source={database};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name IN ('images', 'app_settings');
                """;
            return (long)(command.ExecuteScalar() ?? 0L) == 2;
        }
        catch
        {
            return false;
        }
    }

    private static AppDataPlatformPaths CreatePlatformPaths()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures)) pictures = Path.Combine(home, "Pictures");

        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = Path.Combine(local, "Happy Photon");
            return new(pictures, root, Path.Combine(root, "data"), Path.Combine(root, "cache"));
        }
        if (OperatingSystem.IsMacOS())
        {
            var support = Path.Combine(home, "Library", "Application Support", "Happy Photon");
            return new(
                pictures,
                support,
                Path.Combine(support, "data"),
                Path.Combine(home, "Library", "Caches", "Happy Photon"));
        }

        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        return new(
            pictures,
            Path.Combine(string.IsNullOrWhiteSpace(config) ? Path.Combine(home, ".config") : config, "happy-photon"),
            Path.Combine(string.IsNullOrWhiteSpace(data) ? Path.Combine(home, ".local", "share") : data, "happy-photon"),
            Path.Combine(string.IsNullOrWhiteSpace(cache) ? Path.Combine(home, ".cache") : cache, "happy-photon"));
    }

    private sealed record PersistedLocations(
        int Version,
        string CatalogRoot,
        string CacheRoot,
        bool LegacyCoLocated);
}

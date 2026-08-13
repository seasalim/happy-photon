using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace HappyPhoton.Services;

internal sealed record CatalogIdentity(int Version, Guid CatalogId);
internal sealed record CatalogStamp(int Version, Guid CatalogId, long MaxImageId);

internal static class CatalogCacheStamp
{
    private const string IdentityFileName = ".catalog-identity";
    private const string StampFileName = ".catalog-stamp";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static readonly string[] TierNames =
        ["thumbs", "previews", "rendered-thumbs", "tmp"];

    public static CatalogIdentity EnsureIdentity(
        string catalogRoot,
        out bool created)
    {
        var path = Path.Combine(catalogRoot, IdentityFileName);
        if (TryRead<CatalogIdentity>(path, out var identity) &&
            identity is { Version: 1 } && identity.CatalogId != Guid.Empty)
        {
            created = false;
            return identity;
        }

        identity = new CatalogIdentity(1, Guid.NewGuid());
        created = true;
        AppDataRootOwnership.WriteAtomicOwned(
            catalogRoot,
            path,
            JsonSerializer.Serialize(identity, JsonOptions));
        return identity;
    }

    public static async Task<long> CheckAndRefreshAsync(
        SqliteConnection connection,
        AppDataLocations locations,
        CatalogIdentity identity,
        bool trustedPairing)
    {
        var maxImageId = await ReadMaxImageIdAsync(connection);
        var stampPath = Path.Combine(locations.AssetsRoot, StampFileName);
        var cacheHasArtifacts = TierNames.Any(name =>
            Directory.Exists(Path.Combine(locations.AssetsRoot, name)) &&
            Directory.EnumerateFileSystemEntries(
                Path.Combine(locations.AssetsRoot, name), "*", SearchOption.AllDirectories).Any());
        var stampExists = File.Exists(stampPath);
        var stampReadable = TryRead<CatalogStamp>(stampPath, out var stamp);
        var valid = stampReadable &&
                    stamp is { Version: 1 } &&
                    stamp.CatalogId == identity.CatalogId &&
                    maxImageId >= stamp.MaxImageId;

        if (!valid && cacheHasArtifacts && (stampExists || !trustedPairing))
        {
            ClearTiers(locations.CacheRoot, locations.AssetsRoot);
            foreach (var tierName in TierNames)
            {
                Directory.CreateDirectory(Path.Combine(locations.AssetsRoot, tierName));
            }
        }

        await RefreshAsync(locations, identity, maxImageId);
        return maxImageId;
    }

    public static Task RefreshAsync(
        AppDataLocations locations,
        CatalogIdentity identity,
        long maxImageId)
    {
        var stamp = new CatalogStamp(
            1,
            identity.CatalogId,
            maxImageId);
        Directory.CreateDirectory(locations.AssetsRoot);
        AppDataRootOwnership.WriteAtomicOwned(
            locations.CacheRoot,
            Path.Combine(locations.AssetsRoot, StampFileName),
            JsonSerializer.Serialize(stamp, JsonOptions));
        return Task.CompletedTask;
    }

    public static void ClearTiers(string cacheRoot, string assetsRoot)
    {
        foreach (var tierName in TierNames)
        {
            var tier = Path.Combine(assetsRoot, tierName);
            if (!Directory.Exists(tier)) continue;
            AppDataRootOwnership.AssertAppOwned(cacheRoot);
            Directory.Delete(tier, recursive: true);
        }
    }

    internal static async Task<long> ReadMaxImageIdAsync(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        // images.id is AUTOINCREMENT, so sqlite_sequence holds the never-reused
        // high-water mark; MAX(id) would regress when the newest row is deleted
        // and falsely present a healthy cache as rolled back.
        command.CommandText = """
            SELECT COALESCE(
                (SELECT seq FROM sqlite_sequence WHERE name = 'images'),
                (SELECT COALESCE(MAX(id), 0) FROM images));
            """;
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static bool TryRead<T>(string path, out T? value)
    {
        value = default;
        if (!File.Exists(path)) return false;
        try
        {
            value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
            return value != null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return false;
        }
    }
}

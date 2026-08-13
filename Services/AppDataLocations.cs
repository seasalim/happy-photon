namespace HappyPhoton.Services;

public enum AppDataLocationOrigin
{
    Persisted,
    Environment,
    AdoptedDefault
}

public enum AppDataLocationTopology
{
    Split,
    LegacyCoLocated
}

public sealed record AppDataLocations
{
    public AppDataLocations(
        string catalogRoot,
        string cacheRoot,
        AppDataLocationOrigin catalogOrigin,
        AppDataLocationOrigin cacheOrigin,
        AppDataLocationTopology topology = AppDataLocationTopology.Split)
    {
        CatalogRoot = Normalize(catalogRoot);
        CacheRoot = Normalize(cacheRoot);
        CatalogOrigin = catalogOrigin;
        CacheOrigin = cacheOrigin;
        Topology = topology;
    }

    public string CatalogRoot { get; }
    public string CacheRoot { get; }
    public AppDataLocationOrigin CatalogOrigin { get; }
    public AppDataLocationOrigin CacheOrigin { get; }
    public AppDataLocationTopology Topology { get; }
    public string DatabasePath => Path.Combine(CatalogRoot, "catalog.db");
    public string PresetsRoot => Path.Combine(CatalogRoot, "presets");
    public string AssetsRoot => Path.Combine(CacheRoot, "assets");
    public string TemporaryAssetsRoot => Path.Combine(AssetsRoot, "tmp");
    public bool IsCatalogEnvironmentManaged =>
        CatalogOrigin == AppDataLocationOrigin.Environment;
    public bool IsCacheEnvironmentManaged =>
        CacheOrigin == AppDataLocationOrigin.Environment;

    public AppDataLocations WithCatalog(
        string path,
        AppDataLocationOrigin origin = AppDataLocationOrigin.Persisted) =>
        new(path, CacheRoot, origin, CacheOrigin, AppDataLocationTopology.Split);

    public AppDataLocations WithCache(
        string path,
        AppDataLocationOrigin origin = AppDataLocationOrigin.Persisted) =>
        new(CatalogRoot, path, CatalogOrigin, origin, AppDataLocationTopology.Split);

    private static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}

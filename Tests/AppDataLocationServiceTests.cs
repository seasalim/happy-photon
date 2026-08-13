using HappyPhoton.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AppDataLocationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"happy-photon-locations-{Guid.NewGuid():N}");

    [Fact]
    public async Task FreshInstall_RequiresChoiceThenPersistsSplitDefaults()
    {
        var service = CreateService();

        Assert.Null(await service.ResolveAsync());
        var locations = await service.CreateFreshAsync();
        var reopened = await service.ResolveAsync();

        Assert.Equal(Path.Combine(_root, "Pictures", "Happy Photon Catalog"),
            locations.CatalogRoot);
        Assert.Equal(Path.Combine(_root, "cache", "happy-photon"),
            locations.CacheRoot);
        Assert.Equal(AppDataLocationTopology.Split, locations.Topology);
        Assert.Equal(locations.CatalogRoot, reopened!.CatalogRoot);
        Assert.True(File.Exists(service.PointerPath));
    }

    [Fact]
    public async Task EnvironmentOverrides_AreEffectiveButNeverPersisted()
    {
        var baseService = CreateService();
        var persisted = await baseService.CreateFreshAsync();
        var environmentCatalog = Path.Combine(_root, "environment-catalog");
        var service = CreateService(name => name ==
            AppDataLocationService.CatalogEnvironmentVariable
                ? environmentCatalog
                : null);

        var effective = await service.ResolveAsync();
        await service.PersistAsync(effective!);
        var pointer = await File.ReadAllTextAsync(service.PointerPath);

        Assert.Equal(environmentCatalog, effective!.CatalogRoot);
        Assert.Equal(AppDataLocationOrigin.Environment, effective.CatalogOrigin);
        Assert.Contains(persisted.CatalogRoot.Replace("\\", "\\\\"), pointer);
        Assert.DoesNotContain(environmentCatalog.Replace("\\", "\\\\"), pointer);
    }

    [Fact]
    public async Task LegacyCatalogWithAssets_IsAdoptedInPlace()
    {
        var service = CreateService();
        var catalog = Path.Combine(_root, "Pictures", "Happy Photon Catalog");
        await CreateCatalogSignatureAsync(catalog);
        Directory.CreateDirectory(Path.Combine(catalog, "assets", "thumbs"));

        var locations = await service.ResolveAsync();

        Assert.Equal(catalog, locations!.CatalogRoot);
        Assert.Equal(catalog, locations.CacheRoot);
        Assert.Equal(AppDataLocationTopology.LegacyCoLocated, locations.Topology);
        Assert.True(File.Exists(Path.Combine(catalog,
            AppDataRootOwnership.MarkerFileName)));
    }

    [Fact]
    public async Task PointerLossWithoutAssets_ReAdoptsWithStandardCache()
    {
        var service = CreateService();
        var catalog = Path.Combine(_root, "Pictures", "Happy Photon Catalog");
        await CreateCatalogSignatureAsync(catalog);

        var locations = await service.ResolveAsync();

        Assert.NotEqual(locations!.CatalogRoot, locations.CacheRoot);
        Assert.Equal(AppDataLocationTopology.Split, locations.Topology);
        Assert.Equal(service.StandardCacheRoot, locations.CacheRoot);
    }

    [Fact]
    public async Task CorruptPointer_IsQuarantinedWithoutCreatingCatalog()
    {
        var service = CreateService();
        Directory.CreateDirectory(Path.GetDirectoryName(service.PointerPath)!);
        await File.WriteAllTextAsync(service.PointerPath, "{broken");

        await Assert.ThrowsAsync<AppDataLocationPointerException>(service.ResolveAsync);
        var recovered = await service.QuarantineCorruptPointerAsync();

        Assert.Null(recovered);
        Assert.False(File.Exists(service.PointerPath));
        Assert.Single(Directory.EnumerateFiles(
            Path.GetDirectoryName(service.PointerPath)!, "*.corrupt"));
        Assert.False(Directory.Exists(service.DefaultCatalogRoot));
    }

    [Fact]
    public async Task SemanticallyCorruptPointer_RoutesToRecovery()
    {
        var service = CreateService();
        Directory.CreateDirectory(Path.GetDirectoryName(service.PointerPath)!);
        var root = Path.Combine(_root, "overlap").Replace("\\", "\\\\");
        await File.WriteAllTextAsync(service.PointerPath, $$"""
            {
              "version": 1,
              "catalogRoot": "{{root}}",
              "cacheRoot": "{{root}}\\inside",
              "legacyCoLocated": false
            }
            """);

        await Assert.ThrowsAsync<AppDataLocationPointerException>(service.ResolveAsync);
    }

    [Fact]
    public async Task EnvironmentOnlyFirstRun_OpensWithoutPointerOrPersistence()
    {
        var environmentCatalog = Path.Combine(_root, "environment-catalog");
        var service = CreateService(name => name ==
            AppDataLocationService.CatalogEnvironmentVariable
                ? environmentCatalog
                : null);

        var locations = await service.ResolveAsync();

        Assert.Equal(environmentCatalog, locations!.CatalogRoot);
        Assert.Equal(AppDataLocationOrigin.Environment, locations.CatalogOrigin);
        Assert.Equal(service.StandardCacheRoot, locations.CacheRoot);
        Assert.False(File.Exists(service.PointerPath));
    }

    [Fact]
    public async Task DeletedCacheRoot_SelfHealsFromPointer()
    {
        var service = CreateService();
        var locations = await service.CreateFreshAsync();
        Directory.Delete(locations.CacheRoot, recursive: true);

        var reopened = await service.ResolveAsync();

        Assert.Equal(locations.CacheRoot, reopened!.CacheRoot);
        AppDataRootOwnership.AssertAppOwned(reopened.CacheRoot);
    }

    [Fact]
    public async Task FreshResolverToStartup_CreatesCatalogOnlyAfterChoice()
    {
        var service = CreateService();
        var locations = await service.CreateFreshAsync();
        using var catalog = new CatalogService();

        await catalog.InitializeAsync(locations);

        Assert.True(File.Exists(locations.DatabasePath));
        Assert.True(Directory.Exists(locations.AssetsRoot));
        Assert.False(Directory.Exists(Path.Combine(locations.CatalogRoot, "assets")));
    }

    [Fact]
    public async Task MissingCatalogRoot_IsStartupFailure()
    {
        var service = CreateService();
        var locations = await service.CreateFreshAsync();
        Directory.Delete(locations.CatalogRoot, recursive: true);
        using var catalog = new CatalogService();

        var reopened = await service.ResolveAsync();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            catalog.InitializeAsync(reopened!));
    }

    [Fact]
    public async Task CorruptPointerForCustomCatalog_RequiresExplicitLocate()
    {
        var service = CreateService();
        var custom = Path.Combine(_root, "custom", "Happy Photon Catalog");
        var locations = await service.CreateFreshAsync(catalogRoot: custom);
        using (var catalog = new CatalogService())
        {
            await catalog.InitializeAsync(locations);
        }
        await File.WriteAllTextAsync(service.PointerPath, "invalid");

        Assert.Null(await service.QuarantineCorruptPointerAsync());
        var adopted = await service.AdoptLocatedCatalogAsync(custom);

        Assert.Equal(custom, adopted.CatalogRoot);
        Assert.Equal(service.StandardCacheRoot, adopted.CacheRoot);
    }

    private AppDataLocationService CreateService(
        Func<string, string?>? environment = null) => new(
        new AppDataPlatformPaths(
            Path.Combine(_root, "Pictures"),
            Path.Combine(_root, "pointer"),
            Path.Combine(_root, "data", "happy-photon"),
            Path.Combine(_root, "cache", "happy-photon")),
        environment);

    private static async Task CreateCatalogSignatureAsync(string root)
    {
        Directory.CreateDirectory(root);
        await using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(root, "catalog.db")};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE images (id INTEGER PRIMARY KEY);
            CREATE TABLE app_settings (key TEXT PRIMARY KEY, value TEXT);
            """;
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

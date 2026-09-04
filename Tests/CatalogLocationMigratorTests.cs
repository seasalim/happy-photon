using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogLocationMigratorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"happy-photon-move-{Guid.NewGuid():N}");

    [Fact]
    public async Task CatalogMove_CopiesVerifiesFlipsAndCleansKnownSourceData()
    {
        var (service, locations) = await CreateCatalogAsync("success");
        var destination = Path.Combine(_root, "success-destination");
        AppDataRootOwnership.Claim(destination);
        var migrator = new CatalogLocationMigrator(service);

        await migrator.StageMoveAsync(
            locations, CatalogLocationMoveKind.Catalog, destination);
        await migrator.ExecutePendingAsync();
        var resolved = await service.ResolveAsync();

        Assert.Equal(destination, resolved!.CatalogRoot);
        Assert.True(File.Exists(Path.Combine(destination, "catalog.db")));
        Assert.True(File.Exists(Path.Combine(destination, "presets", "move.json")));
        Assert.False(File.Exists(Path.Combine(locations.CatalogRoot, "catalog.db")));
        Assert.False(File.Exists(migrator.JournalPath));
    }

    [Fact]
    public async Task CatalogMove_FingerprintsCatalogAtExecutionTime()
    {
        var (service, locations) = await CreateCatalogAsync("delayed");
        var destination = Path.Combine(_root, "delayed-destination");
        AppDataRootOwnership.Claim(destination);
        var migrator = new CatalogLocationMigrator(service);
        await migrator.StageMoveAsync(
            locations, CatalogLocationMoveKind.Catalog, destination);
        using (var catalog = new CatalogService())
        {
            await catalog.InitializeAsync(locations);
            await catalog.GetOrCreateImageAsync(Path.Combine(_root, "added-later.jpg"));
        }
        await File.WriteAllTextAsync(
            Path.Combine(locations.PresetsRoot, "move.json"),
            "{\"version\":1,\"name\":\"Changed after staging\"}");

        await migrator.ExecutePendingAsync();

        Assert.Equal(destination, (await service.ResolveAsync())!.CatalogRoot);
        Assert.Contains("Changed after staging", await File.ReadAllTextAsync(
            Path.Combine(destination, "presets", "move.json")));
    }

    [Theory]
    [InlineData(CatalogLocationMovePhase.CatalogCopied)]
    [InlineData(CatalogLocationMovePhase.Verified)]
    public async Task CatalogMove_FailureBeforeFlip_RollsBackWholesale(
        CatalogLocationMovePhase failurePhase)
    {
        var name = failurePhase.ToString();
        var (service, locations) = await CreateCatalogAsync(name);
        var destination = Path.Combine(_root, $"{name}-destination");
        AppDataRootOwnership.Claim(destination);
        await new CatalogLocationMigrator(service).StageMoveAsync(
            locations, CatalogLocationMoveKind.Catalog, destination);
        var migrator = new CatalogLocationMigrator(
            service,
            phase =>
            {
                if (phase == failurePhase) throw new InjectedFailureException();
            });

        await Assert.ThrowsAsync<InjectedFailureException>(migrator.ExecutePendingAsync);

        Assert.True(File.Exists(Path.Combine(locations.CatalogRoot, "catalog.db")));
        Assert.True(File.Exists(Path.Combine(locations.CatalogRoot, "presets", "move.json")));
        Assert.False(File.Exists(Path.Combine(destination, "catalog.db")));
        Assert.False(Directory.Exists(Path.Combine(destination, "presets")));
        Assert.False(File.Exists(migrator.JournalPath));
    }

    [Fact]
    public async Task CatalogMove_FailureAfterFlip_ResumesCleanup()
    {
        var (service, locations) = await CreateCatalogAsync("resume");
        var destination = Path.Combine(_root, "resume-destination");
        AppDataRootOwnership.Claim(destination);
        await new CatalogLocationMigrator(service).StageMoveAsync(
            locations, CatalogLocationMoveKind.Catalog, destination);
        var interrupted = new CatalogLocationMigrator(
            service,
            phase =>
            {
                if (phase == CatalogLocationMovePhase.PointerFlipped)
                    throw new InjectedFailureException();
            });

        await Assert.ThrowsAsync<InjectedFailureException>(interrupted.ExecutePendingAsync);
        Assert.True(File.Exists(interrupted.JournalPath));
        Assert.Equal(destination, (await service.ResolveAsync())!.CatalogRoot);

        await new CatalogLocationMigrator(service).ExecutePendingAsync();

        Assert.False(File.Exists(Path.Combine(locations.CatalogRoot, "catalog.db")));
        Assert.False(File.Exists(interrupted.JournalPath));
    }

    [Fact]
    public async Task CacheMove_OnSameVolume_RenamesAssetsWithoutCopying()
    {
        var (service, locations) = await CreateCatalogAsync("cache");
        var asset = Path.Combine(locations.AssetsRoot, "thumbs", "00", "one.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        await File.WriteAllBytesAsync(asset, [1, 2, 3]);
        var destination = Path.Combine(_root, "cache-destination");
        AppDataRootOwnership.Claim(destination);
        var migrator = new CatalogLocationMigrator(service);

        await migrator.StageMoveAsync(
            locations, CatalogLocationMoveKind.Cache, destination);
        await migrator.ExecutePendingAsync();

        Assert.False(Directory.Exists(locations.AssetsRoot));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(
            Path.Combine(destination, "assets", "thumbs", "00", "one.jpg")));
        Assert.Equal(destination, (await service.ResolveAsync())!.CacheRoot);
    }

    [Theory]
    [InlineData(CatalogLocationMovePhase.Prepared)]
    [InlineData(CatalogLocationMovePhase.CacheMoved)]
    public async Task CacheMove_FailureAtJournalBoundary_RestoresAssets(
        CatalogLocationMovePhase failurePhase)
    {
        var (service, locations) = await CreateCatalogAsync($"cache-{failurePhase}");
        var asset = Path.Combine(locations.AssetsRoot, "thumbs", "00", "one.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        await File.WriteAllBytesAsync(asset, [1, 2, 3]);
        var destination = Path.Combine(_root, $"cache-{failurePhase}-destination");
        AppDataRootOwnership.Claim(destination);
        await new CatalogLocationMigrator(service).StageMoveAsync(
            locations, CatalogLocationMoveKind.Cache, destination);
        var interrupted = new CatalogLocationMigrator(
            service,
            phase =>
            {
                if (phase == failurePhase) throw new InjectedFailureException();
            });

        await Assert.ThrowsAsync<InjectedFailureException>(interrupted.ExecutePendingAsync);

        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(asset));
        Assert.False(File.Exists(interrupted.JournalPath));
    }

    [Theory]
    [InlineData(CatalogLocationMovePhase.Prepared)]
    [InlineData(CatalogLocationMovePhase.CacheMoved)]
    [InlineData(CatalogLocationMovePhase.Verified)]
    public async Task SetAside_FailureBeforeFlip_RestoresBothRoots(
        CatalogLocationMovePhase failurePhase)
    {
        var name = $"aside-{failurePhase}";
        var (service, locations) = await CreateCatalogAsync(name);
        await new CatalogLocationMigrator(service).StageMoveAsync(
            locations, CatalogLocationMoveKind.SetAside);
        var interrupted = new CatalogLocationMigrator(
            service,
            phase =>
            {
                if (phase == failurePhase) throw new InjectedFailureException();
            });

        await Assert.ThrowsAsync<InjectedFailureException>(interrupted.ExecutePendingAsync);

        Assert.True(File.Exists(Path.Combine(locations.CatalogRoot, "catalog.db")));
        Assert.True(Directory.Exists(locations.CacheRoot));
        Assert.False(Directory.EnumerateDirectories(
            Path.GetDirectoryName(locations.CatalogRoot)!, "*.set-aside-*").Any());
    }

    [Fact]
    public async Task SetAside_AfterReplacementIntent_ResumesForward()
    {
        var (service, locations) = await CreateCatalogAsync("aside-resume");
        await new CatalogLocationMigrator(service).StageMoveAsync(
            locations, CatalogLocationMoveKind.SetAside);
        var interrupted = new CatalogLocationMigrator(
            service,
            phase =>
            {
                if (phase == CatalogLocationMovePhase.ReplacementRootsCreated)
                    throw new InjectedFailureException();
            });

        await Assert.ThrowsAsync<InjectedFailureException>(interrupted.ExecutePendingAsync);
        Assert.True(File.Exists(interrupted.JournalPath));

        await new CatalogLocationMigrator(service).ExecutePendingAsync();

        var resolved = await service.ResolveAsync();
        Assert.Equal(service.DefaultCatalogRoot, resolved!.CatalogRoot);
        Assert.Equal(service.StandardCacheRoot, resolved.CacheRoot);
        Assert.False(File.Exists(interrupted.JournalPath));
        Assert.NotEmpty(Directory.EnumerateDirectories(
            _root, "*.set-aside-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task SetAside_RefusesEnvironmentManagedRootWithoutWritingJournal()
    {
        var (service, locations) = await CreateCatalogAsync("managed-aside");
        var managed = new AppDataLocations(
            locations.CatalogRoot,
            locations.CacheRoot,
            AppDataLocationOrigin.Environment,
            AppDataLocationOrigin.Persisted);
        var migrator = new CatalogLocationMigrator(service);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            migrator.StageMoveAsync(managed, CatalogLocationMoveKind.SetAside));

        Assert.False(File.Exists(migrator.JournalPath));
        Assert.True(File.Exists(Path.Combine(locations.CatalogRoot, "catalog.db")));
    }

    [Theory]
    [InlineData(CatalogLocationMoveKind.Catalog)]
    [InlineData(CatalogLocationMoveKind.Cache)]
    public async Task Move_RefusesWhenOtherRootIsEnvironmentManaged(
        CatalogLocationMoveKind kind)
    {
        var (service, locations) = await CreateCatalogAsync($"mixed-{kind}");
        var managed = kind == CatalogLocationMoveKind.Catalog
            ? new AppDataLocations(
                locations.CatalogRoot, locations.CacheRoot,
                AppDataLocationOrigin.Persisted, AppDataLocationOrigin.Environment)
            : new AppDataLocations(
                locations.CatalogRoot, locations.CacheRoot,
                AppDataLocationOrigin.Environment, AppDataLocationOrigin.Persisted);
        var destination = Path.Combine(_root, $"mixed-{kind}-destination");
        AppDataRootOwnership.Claim(destination);
        var migrator = new CatalogLocationMigrator(service);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            migrator.StageMoveAsync(managed, kind, destination));

        Assert.False(File.Exists(migrator.JournalPath));
    }

    [Fact]
    public async Task SetAside_RefusesToAdoptCatalogAlreadyAtDefaultRoot()
    {
        var (service, locations) = await CreateCatalogAsync("stale-default");
        await new CatalogLocationMigrator(service).StageMoveAsync(
            locations, CatalogLocationMoveKind.SetAside);
        using (var stale = new CatalogService(service.DefaultCatalogRoot))
        {
            await stale.InitializeAsync();
            await stale.GetOrCreateImageAsync(Path.Combine(_root, "stale.jpg"));
        }
        var migrator = new CatalogLocationMigrator(service);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            migrator.ExecutePendingAsync);

        Assert.Contains("default location already contains", exception.Message);
        Assert.True(File.Exists(Path.Combine(service.DefaultCatalogRoot, "catalog.db")));
        Assert.True(File.Exists(migrator.JournalPath));
    }

    private async Task<(AppDataLocationService Service, AppDataLocations Locations)>
        CreateCatalogAsync(string name)
    {
        var service = new AppDataLocationService(new AppDataPlatformPaths(
            Path.Combine(_root, $"{name}-Pictures"),
            Path.Combine(_root, $"{name}-pointer"),
            Path.Combine(_root, $"{name}-default-data"),
            Path.Combine(_root, $"{name}-default-cache")));
        var locations = await service.CreateFreshAsync(
            catalogRoot: Path.Combine(_root, $"{name}-catalog"),
            cacheRoot: Path.Combine(_root, $"{name}-cache"));
        using (var catalog = new CatalogService())
        {
            await catalog.InitializeAsync(locations);
            await catalog.GetOrCreateImageAsync(Path.Combine(_root, $"{name}.jpg"));
        }
        Directory.CreateDirectory(locations.PresetsRoot);
        await File.WriteAllTextAsync(
            Path.Combine(locations.PresetsRoot, "move.json"),
            "{\"version\":1,\"name\":\"Move\"}");
        return (service, locations);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class InjectedFailureException : Exception
    {
    }
}

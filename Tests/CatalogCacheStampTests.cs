using System.Text.Json;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogCacheStampTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"happy-photon-stamp-{Guid.NewGuid():N}");

    [Fact]
    public async Task MismatchedCatalogStamp_ClearsKnownTiers()
    {
        var locations = CreateLocations();
        using (var catalog = new CatalogService())
        {
            await catalog.InitializeAsync(locations);
            await catalog.GetOrCreateImageAsync(Path.Combine(_root, "one.jpg"));
        }
        var stale = Path.Combine(locations.AssetsRoot, "thumbs", "00", "stale.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        await File.WriteAllBytesAsync(stale, [1, 2, 3]);
        var stampPath = Path.Combine(locations.AssetsRoot, ".catalog-stamp");
        await File.WriteAllTextAsync(stampPath,
            JsonSerializer.Serialize(new CatalogStamp(1, Guid.NewGuid(), 1)));

        using var reopened = new CatalogService();
        await reopened.InitializeAsync(locations);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(stampPath));
    }

    [Fact]
    public async Task MissingStampOnEstablishedSplitCache_ClearsKnownTiers()
    {
        var locations = CreateLocations();
        using (var catalog = new CatalogService())
        {
            await catalog.InitializeAsync(locations);
        }
        var asset = Path.Combine(locations.AssetsRoot, "previews", "00", "old.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        await File.WriteAllBytesAsync(asset, [1]);
        File.Delete(Path.Combine(locations.AssetsRoot, ".catalog-stamp"));

        using var reopened = new CatalogService();
        await reopened.InitializeAsync(locations);

        Assert.False(File.Exists(asset));
    }

    [Fact]
    public async Task MissingIdentity_IssuesNewGuidAndInvalidatesCache()
    {
        var locations = CreateLocations();
        using (var catalog = new CatalogService())
        {
            await catalog.InitializeAsync(locations);
        }
        var asset = Path.Combine(locations.AssetsRoot, "rendered-thumbs", "00", "old.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        await File.WriteAllBytesAsync(asset, [2]);
        File.Delete(Path.Combine(locations.CatalogRoot, ".catalog-identity"));

        using var reopened = new CatalogService();
        await reopened.InitializeAsync(locations);

        Assert.False(File.Exists(asset));
        Assert.True(File.Exists(Path.Combine(locations.CatalogRoot, ".catalog-identity")));
    }

    [Fact]
    public async Task HighWaterStamp_AdvancesAfterSingleAndBatchInserts()
    {
        var locations = CreateLocations();
        using var catalog = new CatalogService();
        await catalog.InitializeAsync(locations);

        await catalog.GetOrCreateImageAsync(Path.Combine(_root, "one.jpg"));
        Assert.Equal(1, ReadStamp(locations).MaxImageId);
        await catalog.LoadOrCreateImageStatesAsync(
            [Path.Combine(_root, "two.jpg"), Path.Combine(_root, "three.jpg")]);

        Assert.Equal(3, ReadStamp(locations).MaxImageId);
    }

    [Fact]
    public async Task ExistingImageUpsert_DoesNotRewriteStamp()
    {
        var locations = CreateLocations();
        using var catalog = new CatalogService();
        await catalog.InitializeAsync(locations);
        var path = Path.Combine(_root, "existing.jpg");
        await catalog.GetOrCreateImageAsync(path);
        var stampPath = Path.Combine(locations.AssetsRoot, ".catalog-stamp");
        File.SetLastWriteTimeUtc(stampPath, new DateTime(2020, 1, 2, 3, 4, 5,
            DateTimeKind.Utc));
        var unchangedWriteTime = File.GetLastWriteTimeUtc(stampPath);

        await catalog.GetOrCreateImageAsync(path);

        Assert.Equal(unchangedWriteTime, File.GetLastWriteTimeUtc(stampPath));
    }

    [Fact]
    public async Task DeletingNewestImage_DoesNotClearCacheOnRestart()
    {
        var locations = CreateLocations();
        using (var catalog = new CatalogService())
        {
            await catalog.InitializeAsync(locations);
            await catalog.GetOrCreateImageAsync(Path.Combine(_root, "one.jpg"));
            var newest = await catalog.GetOrCreateImageAsync(
                Path.Combine(_root, "two.jpg"));
            await catalog.DeleteImageAsync(newest);
        }
        var asset = Path.Combine(locations.AssetsRoot, "thumbs", "00", "keep.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        await File.WriteAllBytesAsync(asset, [5]);

        using var reopened = new CatalogService();
        await reopened.InitializeAsync(locations);

        Assert.True(File.Exists(asset));
    }

    [Fact]
    public async Task LegacyPairing_BootstrapsMissingStampWithoutClearingCache()
    {
        var root = Path.Combine(_root, "legacy");
        AppDataRootOwnership.Claim(root);
        var locations = new AppDataLocations(
            root, root, AppDataLocationOrigin.AdoptedDefault,
            AppDataLocationOrigin.AdoptedDefault,
            AppDataLocationTopology.LegacyCoLocated);
        var asset = Path.Combine(locations.AssetsRoot, "thumbs", "00", "old.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        await File.WriteAllBytesAsync(asset, [7, 8, 9]);

        using var catalog = new CatalogService();
        await catalog.InitializeAsync(locations);

        Assert.True(File.Exists(asset));
        Assert.True(File.Exists(Path.Combine(locations.AssetsRoot, ".catalog-stamp")));
    }

    [Fact]
    public async Task EstablishedLegacyPairing_ClearsWhenStampLaterDisappears()
    {
        var root = Path.Combine(_root, "established-legacy");
        AppDataRootOwnership.Claim(root);
        var locations = new AppDataLocations(
            root, root, AppDataLocationOrigin.AdoptedDefault,
            AppDataLocationOrigin.AdoptedDefault,
            AppDataLocationTopology.LegacyCoLocated);
        using (var catalog = new CatalogService())
        {
            await catalog.InitializeAsync(locations);
        }
        var asset = Path.Combine(locations.AssetsRoot, "thumbs", "00", "old.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(asset)!);
        await File.WriteAllBytesAsync(asset, [4]);
        File.Delete(Path.Combine(locations.AssetsRoot, ".catalog-stamp"));

        using var reopened = new CatalogService();
        await reopened.InitializeAsync(locations);

        Assert.False(File.Exists(asset));
    }

    private AppDataLocations CreateLocations()
    {
        var catalog = Path.Combine(_root, "catalog");
        var cache = Path.Combine(_root, "cache");
        AppDataRootOwnership.Claim(catalog);
        AppDataRootOwnership.Claim(cache);
        return new AppDataLocations(
            catalog, cache, AppDataLocationOrigin.Persisted,
            AppDataLocationOrigin.Persisted);
    }

    private static CatalogStamp ReadStamp(AppDataLocations locations) =>
        JsonSerializer.Deserialize<CatalogStamp>(File.ReadAllText(
            Path.Combine(locations.AssetsRoot, ".catalog-stamp")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public class CatalogServiceTests
{
    [Fact]
    public void DefaultCatalogPath_UsesHappyPhotonCatalogName()
    {
        using var catalog = new CatalogService();

        Assert.Equal("Happy Photon Catalog", Path.GetFileName(catalog.CatalogPath));
    }

    [Fact]
    public async Task PointerResolvedRoots_WithMissingMarkers_AreReclaimedOnOpen()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonCatalogReclaim_{Guid.NewGuid():N}");
        var catalogRoot = Path.Combine(root, "catalog");
        var cacheRoot = Path.Combine(root, "cache");
        try
        {
            Directory.CreateDirectory(catalogRoot);
            Directory.CreateDirectory(cacheRoot);
            await File.WriteAllBytesAsync(
                Path.Combine(catalogRoot, "catalog.db"), []);
            using var catalog = new CatalogService();
            await catalog.InitializeAsync(new AppDataLocations(
                catalogRoot,
                cacheRoot,
                AppDataLocationOrigin.Persisted,
                AppDataLocationOrigin.Persisted));

            Assert.True(File.Exists(
                Path.Combine(catalogRoot, AppDataRootOwnership.MarkerFileName)));
            Assert.True(File.Exists(
                Path.Combine(cacheRoot, AppDataRootOwnership.MarkerFileName)));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PointerResolvedCatalog_WithoutSignatureOrMarker_Refuses()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonCatalogNoSignature_{Guid.NewGuid():N}");
        var catalogRoot = Path.Combine(root, "catalog");
        try
        {
            Directory.CreateDirectory(catalogRoot);
            await File.WriteAllTextAsync(
                Path.Combine(catalogRoot, "unrelated.txt"), "not ours");
            using var catalog = new CatalogService();

            await Assert.ThrowsAsync<AppDataOwnershipException>(() =>
                catalog.InitializeAsync(new AppDataLocations(
                    catalogRoot,
                    Path.Combine(root, "cache"),
                    AppDataLocationOrigin.Persisted,
                    AppDataLocationOrigin.Persisted)));
            Assert.False(File.Exists(
                Path.Combine(catalogRoot, AppDataRootOwnership.MarkerFileName)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PointerResolvedRoots_WithForeignMarker_StillRefuse()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonCatalogForeign_{Guid.NewGuid():N}");
        var catalogRoot = Path.Combine(root, "catalog");
        try
        {
            Directory.CreateDirectory(catalogRoot);
            await File.WriteAllTextAsync(
                Path.Combine(catalogRoot, AppDataRootOwnership.MarkerFileName),
                "someone else's marker");
            using var catalog = new CatalogService();

            await Assert.ThrowsAsync<AppDataOwnershipException>(() =>
                catalog.InitializeAsync(new AppDataLocations(
                    catalogRoot,
                    Path.Combine(root, "cache"),
                    AppDataLocationOrigin.Persisted,
                    AppDataLocationOrigin.Persisted)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeleteImage_RemovesRenderedCachesAndSidecars()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"HappyPhotonCatalogDelete_{Guid.NewGuid():N}");
        try
        {
            using var catalog = new CatalogService(root);
            await catalog.InitializeAsync();
            var source = Path.Combine(root, "source.jpg");
            await File.WriteAllBytesAsync(source, [1]);
            var catalogId = await catalog.GetOrCreateImageAsync(source);
            var preview = catalog.GetPreviewPath(catalogId);
            var metadata = Path.ChangeExtension(preview, ".meta");
            var renderedThumbnail = catalog.GetRenderedThumbnailPath(catalogId);
            var renderedThumbnailMetadata =
                Path.ChangeExtension(renderedThumbnail, ".meta");
            Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
            Directory.CreateDirectory(Path.GetDirectoryName(renderedThumbnail)!);
            await File.WriteAllBytesAsync(preview, [2]);
            await File.WriteAllTextAsync(metadata, "hash");
            await File.WriteAllBytesAsync(renderedThumbnail, [3]);
            await File.WriteAllTextAsync(renderedThumbnailMetadata, "hash");

            await catalog.DeleteImageAsync(catalogId);

            Assert.False(File.Exists(preview));
            Assert.False(File.Exists(metadata));
            Assert.False(File.Exists(renderedThumbnail));
            Assert.False(File.Exists(renderedThumbnailMetadata));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}

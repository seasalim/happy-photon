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

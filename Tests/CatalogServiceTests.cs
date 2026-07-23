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
}

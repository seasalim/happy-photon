using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogDurabilityTests
{
    [Fact]
    public async Task ProductionCatalogKeepsFullSyncAndDeleteJournal()
    {
        using var fx = new CatalogVmFixture("durability");
        using var catalog = fx.CreateCatalog();
        catalog.SkipDurableSyncForTests = false;
        await catalog.InitializeAsync();

        Assert.Equal("2", await catalog.ReadPragmaForTestsAsync("synchronous"));
        Assert.Equal("delete", await catalog.ReadPragmaForTestsAsync("journal_mode"));
    }

    [Fact]
    public async Task TestCatalogsSkipDurableSyncByDefault()
    {
        using var fx = new CatalogVmFixture("durability");
        using var catalog = await fx.CreateCatalogAsync();

        Assert.True(CatalogService.SkipDurableSyncByDefaultForTests);
        Assert.Equal("0", await catalog.ReadPragmaForTestsAsync("synchronous"));
        Assert.Equal("delete", await catalog.ReadPragmaForTestsAsync("journal_mode"));
    }
}

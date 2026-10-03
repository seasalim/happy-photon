using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class TipsSettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnloadedTipsAreOmittedFromBothSavePaths(bool preferencesOnly)
    {
        using var files = new CatalogVmFixture("unloaded-tips-settings");
        using var catalog = await files.CreateCatalogAsync();
        var service = new AppSettingsService(catalog);
        var keys = new[] { "ShowTips", "BrowseTipsSeen", "DevelopTipsSeen", "ExportTipsSeen" };

        foreach (var seed in new string?[] { null, "True" })
        {
            foreach (var key in keys)
            {
                await catalog.SetAppSettingAsync(key, seed);
            }

            if (preferencesOnly)
            {
                await service.SavePreferencesAsync(new AppSettings(), saveTips: false);
            }
            else
            {
                await service.SaveAsync(new AppSettings(), saveTips: false);
            }

            foreach (var key in keys)
            {
                Assert.Equal(seed, await catalog.GetAppSettingAsync(key));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultsAndAllFlagsSurviveBothSavePaths(bool preferencesOnly)
    {
        using var files = new CatalogVmFixture("tips-settings");
        using var catalog = await files.CreateCatalogAsync();
        var service = new AppSettingsService(catalog);
        var defaults = await service.LoadAsync();
        Assert.True(defaults.ShowTips);
        Assert.False(defaults.BrowseTipsSeen);
        Assert.False(defaults.DevelopTipsSeen);
        Assert.False(defaults.ExportTipsSeen);
        var settings = new AppSettings
        {
            ShowTips = false,
            BrowseTipsSeen = true,
            DevelopTipsSeen = true,
            ExportTipsSeen = true
        };

        if (preferencesOnly)
        {
            await service.SavePreferencesAsync(settings);
        }
        else
        {
            await service.SaveAsync(settings);
        }

        var loaded = await new AppSettingsService(catalog).LoadAsync();
        Assert.False(loaded.ShowTips);
        Assert.True(loaded.BrowseTipsSeen);
        Assert.True(loaded.DevelopTipsSeen);
        Assert.True(loaded.ExportTipsSeen);
    }
}

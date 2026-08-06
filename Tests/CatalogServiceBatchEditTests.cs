using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogServiceBatchEditTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonBatchEdit_{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveEditSettingsBatchAsync_CommitsAllUpdates()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();
        var firstPath = Path.Combine(_tempDirectory, "first.jpg");
        var secondPath = Path.Combine(_tempDirectory, "second.jpg");
        var firstId = await service.GetOrCreateImageAsync(firstPath);
        var secondId = await service.GetOrCreateImageAsync(secondPath);

        await service.SaveEditSettingsBatchAsync(new[]
        {
            new CatalogEditSettingsUpdate(firstId, new EditSettings
            {
                Exposure = 1.5,
                Contrast = 20
            }),
            new CatalogEditSettingsUpdate(secondId, new EditSettings
            {
                Wb = new WhiteBalanceSettings
                {
                    Mode = WbMode.Custom,
                    Kelvin = 7000,
                    Tint = 12
                },
                Saturation = 15
            })
        });

        var states = await service.LoadImageStatesAsync(new[] { firstPath, secondPath });
        Assert.Equal(1.5, states[firstPath].EditSettings.Exposure);
        Assert.Equal(20, states[firstPath].EditSettings.Contrast);
        Assert.Equal(WbMode.Custom, states[secondPath].EditSettings.Wb.Mode);
        Assert.Equal(7000, states[secondPath].EditSettings.Wb.Kelvin);
        Assert.Equal(12, states[secondPath].EditSettings.Wb.Tint);
        Assert.Equal(15, states[secondPath].EditSettings.Saturation);
    }

    [Fact]
    public async Task SaveEditSettingsBatchAsync_RollsBackWhenAnyImageIsMissing()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();
        var firstPath = Path.Combine(_tempDirectory, "first.jpg");
        var secondPath = Path.Combine(_tempDirectory, "second.jpg");
        var firstId = await service.GetOrCreateImageAsync(firstPath);
        var secondId = await service.GetOrCreateImageAsync(secondPath);
        await service.SaveEditSettingsAsync(firstId, new EditSettings { Exposure = 0.5 });
        await service.SaveEditSettingsAsync(secondId, new EditSettings { Exposure = 0.75 });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveEditSettingsBatchAsync(new[]
            {
                new CatalogEditSettingsUpdate(firstId, new EditSettings { Exposure = 4 }),
                new CatalogEditSettingsUpdate(long.MaxValue, new EditSettings { Exposure = 5 }),
                new CatalogEditSettingsUpdate(secondId, new EditSettings { Exposure = 6 })
            }));

        var states = await service.LoadImageStatesAsync(new[] { firstPath, secondPath });
        Assert.Equal(0.5, states[firstPath].EditSettings.Exposure);
        Assert.Equal(0.75, states[secondPath].EditSettings.Exposure);
    }

    [Fact]
    public async Task SaveEditSettingsBatchAsync_EmptyBatchIsNoOp()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();

        await service.SaveEditSettingsBatchAsync(Array.Empty<CatalogEditSettingsUpdate>());

        Assert.Empty(await service.LoadImageStatesAsync(Array.Empty<string>()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}

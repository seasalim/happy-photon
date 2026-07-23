using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogServiceRatingTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonRatingTests_{Guid.NewGuid():N}");
    private CatalogService? _service;

    private async Task<CatalogService> CreateServiceAsync()
    {
        _service = new CatalogService(_tempDirectory);
        await _service.InitializeAsync();
        return _service;
    }

    [Fact]
    public async Task SaveAndLoadRating_RoundTrips()
    {
        var service = await CreateServiceAsync();
        const string path = @"C:\photos\a.jpg";
        var id = await service.GetOrCreateImageAsync(path);

        await service.SaveRatingAsync(id, 4);

        Assert.Equal(4, await LoadRatingAsync(service, path));
    }

    [Fact]
    public async Task LoadRating_DefaultsToZeroForFreshRow()
    {
        var service = await CreateServiceAsync();
        const string path = @"C:\photos\a.jpg";
        await service.GetOrCreateImageAsync(path);

        Assert.Equal(0, await LoadRatingAsync(service, path));
    }

    [Fact]
    public async Task LoadRating_ReturnsZeroForMissingRow()
    {
        var service = await CreateServiceAsync();

        var states = await service.LoadImageStatesAsync(new[] { @"C:\photos\missing.jpg" });
        Assert.Empty(states);
    }

    [Fact]
    public async Task SaveRating_ClampsOutOfRangeValues()
    {
        var service = await CreateServiceAsync();
        const string path = @"C:\photos\a.jpg";
        var id = await service.GetOrCreateImageAsync(path);

        await service.SaveRatingAsync(id, 9);
        Assert.Equal(5, await LoadRatingAsync(service, path));

        await service.SaveRatingAsync(id, -3);
        Assert.Equal(0, await LoadRatingAsync(service, path));
    }

    private static async Task<int> LoadRatingAsync(CatalogService service, string path)
    {
        var states = await service.LoadImageStatesAsync(new[] { path });
        return states[path].Rating;
    }

    public void Dispose()
    {
        _service?.Dispose();
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}

using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogServiceConcurrencyTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonCatalogConcurrency_{Guid.NewGuid():N}");

    [Fact]
    public async Task ConcurrentOperations_OnSharedConnectionRemainConsistent()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();
        var paths = Enumerable.Range(0, 700)
            .Select(index => Path.Combine(_tempDirectory, $"image-{index}.jpg"))
            .ToArray();
        var ids = new long[40];
        for (var index = 0; index < ids.Length; index++)
        {
            ids[index] = await service.GetOrCreateImageAsync(paths[index]);
        }

        var folderLoads = Enumerable.Range(0, 4)
            .Select(index => service.LoadOrCreateImageStatesAsync(
                paths.Skip(index * 100).Take(400).ToArray()));
        var writes = ids.Select((id, index) => Task.WhenAll(
            service.SaveEditSettingsAsync(id, new EditSettings { Exposure = index / 10.0 }),
            service.SaveFlagStateAsync(id, index % 2 == 0 ? ImageFlag.Picked : ImageFlag.Unflagged),
            service.SaveRatingAsync(id, index % 6),
            service.SetAppSettingAsync($"key-{index}", index.ToString())));

        await Task.WhenAll(folderLoads.Cast<Task>().Concat(writes));

        var states = await service.LoadImageStatesAsync(paths);
        Assert.Equal(paths.Length, states.Count);
        Assert.Equal(paths.Length, states.Values.Select(state => state.CatalogId).Distinct().Count());
        for (var index = 0; index < ids.Length; index++)
        {
            Assert.Equal(index.ToString(), await service.GetAppSettingAsync($"key-{index}"));
        }
    }

    [Fact]
    public async Task ConcurrentGetOrCreate_ReturnsOneIdAndPreservesState()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();
        var path = Path.Combine(_tempDirectory, "same.jpg");

        var ids = await Task.WhenAll(Enumerable.Range(0, 50)
            .Select(_ => service.GetOrCreateImageAsync(path)));
        await service.SaveRatingAsync(ids[0], 4);
        var secondId = await service.GetOrCreateImageAsync(path.ToUpperInvariant());

        Assert.Single(ids.Distinct());
        Assert.Equal(ids[0], secondId);
        var states = await service.LoadImageStatesAsync(new[] { path });
        Assert.Equal(4, states[path].Rating);
    }

    [Fact]
    public async Task FailedCommand_ReleasesConnectionGate()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => service.GetAppSettingAsync(null!));
        await service.SetAppSettingAsync("healthy", "yes");

        Assert.Equal("yes", await service.GetAppSettingAsync("healthy"));
    }

    [Fact]
    public async Task CancelledFolderLoad_DoesNotPoisonLaterOperations()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.LoadOrCreateImageStatesAsync(
                new[] { Path.Combine(_tempDirectory, "cancelled.jpg") },
                cancellation.Token));

        var id = await service.GetOrCreateImageAsync(Path.Combine(_tempDirectory, "healthy.jpg"));
        Assert.True(id > 0);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}

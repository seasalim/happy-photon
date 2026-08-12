using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogServiceImageStateTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"HappyPhotonTests_{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadImageStatesAsync_ReturnsSavedStateForRequestedPaths()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();

        var firstPath = Path.Combine(_tempDirectory, "first.jpg");
        var secondPath = Path.Combine(_tempDirectory, "second.jpg");
        var firstId = await service.GetOrCreateImageAsync(firstPath);
        var secondId = await service.GetOrCreateImageAsync(secondPath);

        await service.SaveEditSettingsAsync(firstId, new EditSettings
        {
            Exposure = 1.25,
            Contrast = 20
        });
        await service.SaveFlagStateAsync(firstId, ImageFlag.Picked);
        await service.SaveRatingAsync(firstId, 4);
        await service.SaveColorLabelAsync([firstId], ColorLabel.Blue);

        var states = await service.LoadImageStatesAsync(new[]
        {
            firstPath,
            secondPath,
            Path.Combine(_tempDirectory, "missing.jpg")
        });

        Assert.Equal(2, states.Count);
        Assert.Equal(firstId, states[firstPath].CatalogId);
        Assert.Equal(1.25, states[firstPath].EditSettings.Exposure);
        Assert.Equal(20, states[firstPath].EditSettings.Contrast);
        Assert.Equal(ImageFlag.Picked, states[firstPath].Flag);
        Assert.Equal(4, states[firstPath].Rating);
        Assert.Equal(ColorLabel.Blue, states[firstPath].ColorLabel);
        Assert.Equal(secondId, states[secondPath].CatalogId);
        Assert.Equal(
            WbMode.AsShot,
            states[secondPath].EditSettings.Wb.Mode);
        Assert.Equal(
            EditSettings.CurrentVersion,
            states[secondPath].EditSettings.Version);
    }

    [Fact]
    public async Task LoadImageStatesAsync_HandlesEmptyPathCollection()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();

        var states = await service.LoadImageStatesAsync(Array.Empty<string>());

        Assert.Empty(states);
    }

    [Fact]
    public async Task LoadOrCreateImageStatesAsync_CreatesMissingRecordsAndPreservesState()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();

        var existingPath = Path.Combine(_tempDirectory, "existing.jpg");
        var newPath = Path.Combine(_tempDirectory, "new.jpg");
        var existingId = await service.GetOrCreateImageAsync(existingPath);
        await service.SaveRatingAsync(existingId, 5);

        var states = await service.LoadOrCreateImageStatesAsync(new[] { existingPath, newPath });

        Assert.Equal(2, states.Count);
        Assert.Equal(existingId, states[existingPath].CatalogId);
        Assert.Equal(5, states[existingPath].Rating);
        Assert.True(states[newPath].CatalogId > 0);
        Assert.Equal(WbMode.AsShot, states[newPath].EditSettings.Wb.Mode);
        Assert.Equal(
            EditSettings.CurrentVersion,
            states[newPath].EditSettings.Version);
    }

    [Fact]
    public async Task LoadOrCreateImageStatesAsync_HandlesMultipleBatchesAndDuplicatePaths()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();

        var paths = Enumerable.Range(0, 350)
            .Select(index => Path.Combine(_tempDirectory, $"image-{index}.jpg"))
            .ToArray();
        var requestedPaths = paths.Concat(new[] { paths[0], paths[349] }).ToArray();

        var states = await service.LoadOrCreateImageStatesAsync(requestedPaths);

        Assert.Equal(paths.Length, states.Count);
        Assert.Equal(paths.Length, states.Values.Select(state => state.CatalogId).Distinct().Count());
    }

    [Fact]
    public async Task CatalogPathLookups_PreserveStateAcrossPathCasingChanges()
    {
        using var service = new CatalogService(_tempDirectory);
        await service.InitializeAsync();

        var originalPath = Path.Combine(_tempDirectory, "IMAGE.JPG");
        var changedCasePath = Path.Combine(_tempDirectory, "image.jpg");
        var originalId = await service.GetOrCreateImageAsync(originalPath);
        await service.SaveRatingAsync(originalId, 3);

        var states = await service.LoadOrCreateImageStatesAsync(new[] { changedCasePath });
        var lookupId = await service.GetOrCreateImageAsync(changedCasePath);

        Assert.Single(states);
        Assert.Equal(originalId, states[changedCasePath].CatalogId);
        Assert.Equal(3, states[changedCasePath].Rating);
        Assert.Equal(originalId, lookupId);
    }

    [Fact]
    public async Task InitializeAsync_ClearsTemporaryThumbnailAssets()
    {
        var temporaryAssets = Path.Combine(_tempDirectory, "assets", "tmp");
        Directory.CreateDirectory(temporaryAssets);
        File.WriteAllText(Path.Combine(temporaryAssets, "orphan.jpg"), "orphan");
        using var service = new CatalogService(_tempDirectory);

        await service.InitializeAsync();

        Assert.True(Directory.Exists(temporaryAssets));
        Assert.Empty(Directory.GetFiles(temporaryAssets));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}

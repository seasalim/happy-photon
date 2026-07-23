using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class CatalogIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"HappyPhotonCatalogIdentity_{Guid.NewGuid():N}");

    [Fact]
    public async Task FreshHappyPhotonCatalog_ReopensWithoutLosingState()
    {
        var catalogPath = Path.Combine(_root, "Happy Photon Catalog");
        var photoPath = CreateSourcePhoto();
        long catalogId;

        using (var catalog = new CatalogService(catalogPath))
        {
            await catalog.InitializeAsync();
            catalogId = await catalog.GetOrCreateImageAsync(photoPath);
            await catalog.SaveRatingAsync(catalogId, 3);
            await catalog.SetAppSettingAsync("identity-test", "saved");
        }

        using var reopened = new CatalogService(catalogPath);
        await reopened.InitializeAsync();
        var states = await reopened.LoadImageStatesAsync(new[] { photoPath });

        Assert.True(File.Exists(Path.Combine(catalogPath, "catalog.db")));
        Assert.Equal(catalogId, states[photoPath].CatalogId);
        Assert.Equal(3, states[photoPath].Rating);
        Assert.Equal("saved", await reopened.GetAppSettingAsync("identity-test"));
    }

    [Fact]
    public async Task ClosedPhotoEditCatalog_RenamedToHappyPhoton_PreservesStateAndAssets()
    {
        var oldCatalogPath = Path.Combine(_root, "PhotoEdit Catalog");
        var happyPhotonCatalogPath = Path.Combine(_root, "Happy Photon Catalog");
        var photoPath = CreateSourcePhoto();
        long catalogId;
        string presetId;
        string thumbnailRelativePath;
        string previewRelativePath;

        using (var catalog = new CatalogService(oldCatalogPath))
        {
            await catalog.InitializeAsync();
            var presets = new PresetService(Path.Combine(catalog.CatalogPath, "presets"));
            await presets.InitializeAsync();
            var preset = await presets.SaveUserPresetAsync(
                "Identity test",
                new EditSettings { Exposure = 0.5, Contrast = 12 });
            presetId = preset.Id;

            catalogId = await catalog.GetOrCreateImageAsync(photoPath);
            await catalog.SaveEditSettingsAsync(catalogId, new EditSettings
            {
                Exposure = 1.25,
                Temperature = 8,
                Rotation = 90,
                AppliedPresetId = presetId
            });
            await catalog.SaveFlagStateAsync(catalogId, ImageFlag.Picked);
            await catalog.SaveRatingAsync(catalogId, 5);
            await catalog.SetAppSettingAsync("McpServerEnabled", "true");

            var thumbnailPath = catalog.GetThumbnailPath(catalogId);
            var previewPath = catalog.GetPreviewPath(catalogId);
            Directory.CreateDirectory(Path.GetDirectoryName(thumbnailPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(previewPath)!);
            await File.WriteAllBytesAsync(thumbnailPath, [1, 2, 3]);
            await File.WriteAllBytesAsync(previewPath, [4, 5, 6]);
            thumbnailRelativePath = Path.GetRelativePath(oldCatalogPath, thumbnailPath);
            previewRelativePath = Path.GetRelativePath(oldCatalogPath, previewPath);
        }

        Directory.Move(oldCatalogPath, happyPhotonCatalogPath);

        using var reopened = new CatalogService(happyPhotonCatalogPath);
        await reopened.InitializeAsync();
        var states = await reopened.LoadImageStatesAsync(new[] { photoPath });
        var state = states[photoPath];
        var presetsAfterRename = new PresetService(
            Path.Combine(reopened.CatalogPath, "presets"));
        await presetsAfterRename.InitializeAsync();

        Assert.True(Path.IsPathFullyQualified(photoPath));
        Assert.True(File.Exists(photoPath));
        Assert.Equal(catalogId, state.CatalogId);
        Assert.Equal(1.25, state.EditSettings.Exposure);
        Assert.Equal(8, state.EditSettings.Temperature);
        Assert.Equal(90, state.EditSettings.Rotation);
        Assert.Equal(presetId, state.EditSettings.AppliedPresetId);
        Assert.Equal(ImageFlag.Picked, state.Flag);
        Assert.Equal(5, state.Rating);
        Assert.Equal("true", await reopened.GetAppSettingAsync("McpServerEnabled"));
        Assert.NotNull(presetsAfterRename.GetById(presetId));
        Assert.Equal(
            [1, 2, 3],
            await File.ReadAllBytesAsync(
                Path.Combine(happyPhotonCatalogPath, thumbnailRelativePath)));
        Assert.Equal(
            [4, 5, 6],
            await File.ReadAllBytesAsync(
                Path.Combine(happyPhotonCatalogPath, previewRelativePath)));
        Assert.False(Directory.Exists(oldCatalogPath));
    }

    private string CreateSourcePhoto()
    {
        var photosPath = Path.Combine(_root, "photos");
        Directory.CreateDirectory(photosPath);
        var photoPath = Path.GetFullPath(Path.Combine(photosPath, "source.jpg"));
        File.WriteAllBytes(photoPath, [0xff, 0xd8, 0xff, 0xd9]);
        return photoPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}

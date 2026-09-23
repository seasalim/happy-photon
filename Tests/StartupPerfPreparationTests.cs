using HappyPhoton.Models;
using HappyPhoton.Services;
using HappyPhoton.ViewModels;
using Xunit;

namespace HappyPhoton.Tests;

// Seeds the isolated catalog, cache, and restored folder that
// scripts/startup-perf.ps1 launches the published app against, so startup
// measurements never open the user's own catalog.
public sealed class StartupPerfPreparationTests
{
    private const int PhotoCount = 200;

    [Fact]
    public async Task PrepareIsolatedStartup()
    {
        var root = Environment.GetEnvironmentVariable("HAPPY_PHOTON_STARTUP_PERF_ROOT");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(root), "Use scripts/startup-perf.ps1.");
        var catalogRoot = Path.Combine(root, "catalog");
        var cacheRoot = Path.Combine(root, "cache");
        var photos = Path.Combine(root, "photos");
        Assert.False(Directory.Exists(catalogRoot), "Seed into a fresh root.");

        Directory.CreateDirectory(photos);
        var source = GoldenTestPaths.Asset("srgb-reference.jpg");
        var paths = Enumerable.Range(0, PhotoCount)
            .Select(index => Path.Combine(photos, $"image-{index:D3}.jpg"))
            .ToArray();
        foreach (var path in paths) File.Copy(source, path);

        AppDataRootOwnership.ClaimFresh(catalogRoot);
        AppDataRootOwnership.ClaimFresh(cacheRoot);
        using var catalog = new CatalogService();
        await catalog.InitializeAsync(new AppDataLocations(
            catalogRoot,
            cacheRoot,
            AppDataLocationOrigin.Environment,
            AppDataLocationOrigin.Environment));
        await catalog.LoadOrCreateImageStatesAsync(paths);
        await new AppSettingsService(catalog).SaveAsync(new AppSettings
        {
            RootFolderPath = photos,
            SelectedFolderPath = photos,
            FirstRunExperienceVersion = MainWindowViewModel.CurrentFirstRunExperienceVersion
        });

        Assert.True(AppDataLocationService.HasCatalogSignature(catalogRoot));
    }
}

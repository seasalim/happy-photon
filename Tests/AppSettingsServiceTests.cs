using HappyPhoton.Models;
using HappyPhoton.Services;
using Xunit;

namespace HappyPhoton.Tests;

public sealed class AppSettingsServiceTests : IDisposable
{
    private readonly string _catalogPath =
        Path.Combine(Path.GetTempPath(), $"happy-photon-settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoad_RoundTripsNullableFirstRunVersion()
    {
        using var catalog = new CatalogService(_catalogPath);
        await catalog.InitializeAsync();
        var service = new AppSettingsService(catalog);

        var empty = await service.LoadAsync();
        Assert.Null(empty.FirstRunExperienceVersion);
        Assert.False(empty.StripLocationData);
        Assert.Equal(OutputSharpeningMode.Screen, empty.OutputSharpening);
        Assert.Equal(BrowseThumbnailSize.Medium, empty.BrowseThumbnailSize);
        Assert.Equal(AppTheme.Dark, empty.AppTheme);

        await service.SaveAsync(new AppSettings
        {
            RootFolderPath = @"C:\Photos",
            SelectedFolderPath = @"C:\Photos\Shoot",
            FirstRunExperienceVersion = 1,
            FileTypeFilter = ImageFileTypeFilter.Raw,
            BrowseThumbnailSize = BrowseThumbnailSize.Large,
            AppTheme = AppTheme.MidGray,
            StripLocationData = true,
            OutputSharpening = OutputSharpeningMode.Print
        });

        var loaded = await service.LoadAsync();
        Assert.Equal(1, loaded.FirstRunExperienceVersion);
        Assert.Equal(@"C:\Photos", loaded.RootFolderPath);
        Assert.Equal(@"C:\Photos\Shoot", loaded.SelectedFolderPath);
        Assert.Equal(ImageFileTypeFilter.Raw, loaded.FileTypeFilter);
        Assert.Equal(BrowseThumbnailSize.Large, loaded.BrowseThumbnailSize);
        Assert.Equal(AppTheme.MidGray, loaded.AppTheme);
        Assert.True(loaded.StripLocationData);
        Assert.Equal(OutputSharpeningMode.Print, loaded.OutputSharpening);
    }

    [Fact]
    public async Task LoadAsync_DoesNotTurnCatalogFailureIntoEmptySettings()
    {
        using var catalog = new CatalogService(_catalogPath);
        var service = new AppSettingsService(catalog);

        await Assert.ThrowsAsync<InvalidOperationException>(service.LoadAsync);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("enormous")]
    [InlineData("99")]
    public async Task LoadAsync_InvalidThumbnailSizeDefaultsToMedium(string? value)
    {
        using var catalog = new CatalogService(_catalogPath);
        await catalog.InitializeAsync();
        await catalog.SetAppSettingAsync("BrowseThumbnailSize", value);

        var loaded = await new AppSettingsService(catalog).LoadAsync();

        Assert.Equal(BrowseThumbnailSize.Medium, loaded.BrowseThumbnailSize);
    }

    [Fact]
    public async Task LoadAsync_ParsesThumbnailSizeCaseInsensitively()
    {
        using var catalog = new CatalogService(_catalogPath);
        await catalog.InitializeAsync();
        await catalog.SetAppSettingAsync("BrowseThumbnailSize", "large");

        var loaded = await new AppSettingsService(catalog).LoadAsync();

        Assert.Equal(BrowseThumbnailSize.Large, loaded.BrowseThumbnailSize);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("light")]
    [InlineData("99")]
    public async Task LoadAsync_InvalidThemeDefaultsToDark(string? value)
    {
        using var catalog = new CatalogService(_catalogPath);
        await catalog.InitializeAsync();
        await catalog.SetAppSettingAsync("AppTheme", value);

        var loaded = await new AppSettingsService(catalog).LoadAsync();

        Assert.Equal(AppTheme.Dark, loaded.AppTheme);
    }

    [Fact]
    public async Task LoadAsync_ParsesThemeCaseInsensitively()
    {
        using var catalog = new CatalogService(_catalogPath);
        await catalog.InitializeAsync();
        await catalog.SetAppSettingAsync("AppTheme", "midgray");

        var loaded = await new AppSettingsService(catalog).LoadAsync();

        Assert.Equal(AppTheme.MidGray, loaded.AppTheme);
    }

    [Theory]
    [InlineData("True", OutputSharpeningMode.Screen)]
    [InlineData("False", OutputSharpeningMode.Off)]
    [InlineData(null, OutputSharpeningMode.Screen)]
    [InlineData("invalid", OutputSharpeningMode.Screen)]
    [InlineData("print", OutputSharpeningMode.Print)]
    public async Task LoadAsync_MigratesOutputSharpeningCatalogString(
        string? value,
        OutputSharpeningMode expected)
    {
        using var catalog = new CatalogService(_catalogPath);
        await catalog.InitializeAsync();
        if (value != null)
        {
            await catalog.SetAppSettingAsync("OutputSharpening", value);
        }

        var loaded = await new AppSettingsService(catalog).LoadAsync();

        Assert.Equal(expected, loaded.OutputSharpening);
    }

    [Fact]
    public async Task SavePreferences_DoesNotChangeFolderOrFirstRunState()
    {
        using var catalog = new CatalogService(_catalogPath);
        await catalog.InitializeAsync();
        var service = new AppSettingsService(catalog);
        await service.SaveAsync(new AppSettings
        {
            RootFolderPath = "root",
            SelectedFolderPath = "selected",
            FirstRunExperienceVersion = 1
        });

        await service.SavePreferencesAsync(new AppSettings
        {
            FileTypeFilter = ImageFileTypeFilter.Jpeg,
            BrowseThumbnailSize = BrowseThumbnailSize.Small,
            AppTheme = AppTheme.MidGray,
            StripLocationData = true,
            OutputSharpening = OutputSharpeningMode.Off
        });

        var loaded = await service.LoadAsync();
        Assert.Equal("root", loaded.RootFolderPath);
        Assert.Equal("selected", loaded.SelectedFolderPath);
        Assert.Equal(1, loaded.FirstRunExperienceVersion);
        Assert.Equal(ImageFileTypeFilter.Jpeg, loaded.FileTypeFilter);
        Assert.Equal(BrowseThumbnailSize.Small, loaded.BrowseThumbnailSize);
        Assert.Equal(AppTheme.MidGray, loaded.AppTheme);
        Assert.True(loaded.StripLocationData);
        Assert.Equal(OutputSharpeningMode.Off, loaded.OutputSharpening);
    }

    public void Dispose()
    {
        if (Directory.Exists(_catalogPath))
        {
            Directory.Delete(_catalogPath, recursive: true);
        }
    }
}

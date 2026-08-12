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
        Assert.True(empty.OutputSharpening);
        Assert.Equal(LibraryThumbnailSize.Medium, empty.LibraryThumbnailSize);
        Assert.Equal(AppTheme.Dark, empty.AppTheme);

        await service.SaveAsync(new AppSettings
        {
            RootFolderPath = @"C:\Photos",
            SelectedFolderPath = @"C:\Photos\Shoot",
            FirstRunExperienceVersion = 1,
            FileTypeFilter = ImageFileTypeFilter.Raw,
            LibraryThumbnailSize = LibraryThumbnailSize.Large,
            AppTheme = AppTheme.MidGrey,
            StripLocationData = true,
            OutputSharpening = false,
            McpServerEnabled = true,
            McpToken = "12345678901234567890123456789012"
        });

        var loaded = await service.LoadAsync();
        Assert.Equal(1, loaded.FirstRunExperienceVersion);
        Assert.Equal(@"C:\Photos", loaded.RootFolderPath);
        Assert.Equal(@"C:\Photos\Shoot", loaded.SelectedFolderPath);
        Assert.Equal(ImageFileTypeFilter.Raw, loaded.FileTypeFilter);
        Assert.Equal(LibraryThumbnailSize.Large, loaded.LibraryThumbnailSize);
        Assert.Equal(AppTheme.MidGrey, loaded.AppTheme);
        Assert.True(loaded.StripLocationData);
        Assert.False(loaded.OutputSharpening);
        Assert.True(loaded.McpServerEnabled);
        Assert.Equal("12345678901234567890123456789012", loaded.McpToken);
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
        await catalog.SetAppSettingAsync("LibraryThumbnailSize", value);

        var loaded = await new AppSettingsService(catalog).LoadAsync();

        Assert.Equal(LibraryThumbnailSize.Medium, loaded.LibraryThumbnailSize);
    }

    [Fact]
    public async Task LoadAsync_ParsesThumbnailSizeCaseInsensitively()
    {
        using var catalog = new CatalogService(_catalogPath);
        await catalog.InitializeAsync();
        await catalog.SetAppSettingAsync("LibraryThumbnailSize", "large");

        var loaded = await new AppSettingsService(catalog).LoadAsync();

        Assert.Equal(LibraryThumbnailSize.Large, loaded.LibraryThumbnailSize);
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
        await catalog.SetAppSettingAsync("AppTheme", "midgrey");

        var loaded = await new AppSettingsService(catalog).LoadAsync();

        Assert.Equal(AppTheme.MidGrey, loaded.AppTheme);
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
            LibraryThumbnailSize = LibraryThumbnailSize.Small,
            AppTheme = AppTheme.MidGrey,
            StripLocationData = true,
            OutputSharpening = false,
            McpServerEnabled = true,
            McpToken = "token"
        });

        var loaded = await service.LoadAsync();
        Assert.Equal("root", loaded.RootFolderPath);
        Assert.Equal("selected", loaded.SelectedFolderPath);
        Assert.Equal(1, loaded.FirstRunExperienceVersion);
        Assert.Equal(ImageFileTypeFilter.Jpeg, loaded.FileTypeFilter);
        Assert.Equal(LibraryThumbnailSize.Small, loaded.LibraryThumbnailSize);
        Assert.Equal(AppTheme.MidGrey, loaded.AppTheme);
        Assert.True(loaded.StripLocationData);
        Assert.False(loaded.OutputSharpening);
        Assert.True(loaded.McpServerEnabled);
        Assert.Equal("token", loaded.McpToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(_catalogPath))
        {
            Directory.Delete(_catalogPath, recursive: true);
        }
    }
}

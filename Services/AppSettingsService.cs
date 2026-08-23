using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Service for loading and saving application settings via the catalog.
/// </summary>
public class AppSettingsService
{
    private readonly CatalogService _catalogService;

    private const string RootFolderPathKey = "RootFolderPath";
    private const string SelectedFolderPathKey = "SelectedFolderPath";
    private const string FirstRunExperienceVersionKey = "FirstRunExperienceVersion";
    private const string FileTypeFilterKey = "FileTypeFilter";
    private const string LibraryThumbnailSizeKey = "LibraryThumbnailSize";
    private const string AppThemeKey = "AppTheme";
    private const string StripLocationDataKey = "StripLocationData";
    private const string OutputSharpeningKey = "OutputSharpening";

    public AppSettingsService(CatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public async Task<AppSettings> LoadAsync()
    {
        var fileTypeFilter = ImageFileTypeFilter.All;
        var savedFilter = await _catalogService.GetAppSettingAsync(FileTypeFilterKey);
        if (!string.IsNullOrEmpty(savedFilter) &&
            Enum.TryParse<ImageFileTypeFilter>(savedFilter, ignoreCase: true, out var parsedFilter))
        {
            fileTypeFilter = parsedFilter;
        }

        var thumbnailSize = LibraryThumbnailSize.Medium;
        var savedThumbnailSize = await _catalogService.GetAppSettingAsync(LibraryThumbnailSizeKey);
        if (!string.IsNullOrEmpty(savedThumbnailSize) &&
            Enum.TryParse<LibraryThumbnailSize>(savedThumbnailSize, ignoreCase: true, out var parsedSize) &&
            Enum.IsDefined(parsedSize))
        {
            thumbnailSize = parsedSize;
        }

        var appTheme = AppTheme.Dark;
        var savedAppTheme = await _catalogService.GetAppSettingAsync(AppThemeKey);
        if (!string.IsNullOrEmpty(savedAppTheme) &&
            Enum.TryParse<AppTheme>(savedAppTheme, ignoreCase: true, out var parsedTheme) &&
            Enum.IsDefined(parsedTheme))
        {
            appTheme = parsedTheme;
        }

        int? firstRunExperienceVersion = null;
        var savedFirstRunVersion =
            await _catalogService.GetAppSettingAsync(FirstRunExperienceVersionKey);
        if (int.TryParse(savedFirstRunVersion, out var parsedFirstRunVersion))
        {
            firstRunExperienceVersion = parsedFirstRunVersion;
        }

        return new AppSettings
        {
            RootFolderPath = await _catalogService.GetAppSettingAsync(RootFolderPathKey),
            SelectedFolderPath = await _catalogService.GetAppSettingAsync(SelectedFolderPathKey),
            FirstRunExperienceVersion = firstRunExperienceVersion,
            FileTypeFilter = fileTypeFilter,
            LibraryThumbnailSize = thumbnailSize,
            AppTheme = appTheme,
            StripLocationData = bool.TryParse(
                await _catalogService.GetAppSettingAsync(StripLocationDataKey),
                out var stripLocationData) && stripLocationData,
            OutputSharpening = ParseOutputSharpening(
                await _catalogService.GetAppSettingAsync(OutputSharpeningKey))
        };
    }

    public Task SaveAsync(AppSettings settings)
    {
        return _catalogService.SetAppSettingsAsync(new Dictionary<string, string?>
        {
            [RootFolderPathKey] = settings.RootFolderPath,
            [SelectedFolderPathKey] = settings.SelectedFolderPath,
            [FirstRunExperienceVersionKey] =
                settings.FirstRunExperienceVersion?.ToString(),
            [FileTypeFilterKey] = settings.FileTypeFilter.ToString(),
            [LibraryThumbnailSizeKey] = settings.LibraryThumbnailSize.ToString(),
            [AppThemeKey] = settings.AppTheme.ToString(),
            [StripLocationDataKey] = settings.StripLocationData.ToString(),
            [OutputSharpeningKey] = settings.OutputSharpening.ToString()
        });
    }

    public Task SavePreferencesAsync(AppSettings settings)
    {
        return _catalogService.SetAppSettingsAsync(new Dictionary<string, string?>
        {
            [FileTypeFilterKey] = settings.FileTypeFilter.ToString(),
            [LibraryThumbnailSizeKey] = settings.LibraryThumbnailSize.ToString(),
            [AppThemeKey] = settings.AppTheme.ToString(),
            [StripLocationDataKey] = settings.StripLocationData.ToString(),
            [OutputSharpeningKey] = settings.OutputSharpening.ToString()
        });
    }

    public Task SaveFirstRunVersionAsync(int version)
    {
        return _catalogService.SetAppSettingAsync(
            FirstRunExperienceVersionKey,
            version.ToString());
    }

    private static OutputSharpeningMode ParseOutputSharpening(string? value)
    {
        if (Enum.TryParse<OutputSharpeningMode>(
                value,
                ignoreCase: true,
                out var mode) &&
            Enum.IsDefined(mode))
        {
            return mode;
        }

        return bool.TryParse(value, out var legacyEnabled)
            ? legacyEnabled
                ? OutputSharpeningMode.Screen
                : OutputSharpeningMode.Off
            : OutputSharpeningMode.Screen;
    }
}

using HappyPhoton.Models;

namespace HappyPhoton.Services;

/// <summary>
/// Service for loading and saving application settings via the catalog.
/// </summary>
public class AppSettingsService
{
    private readonly ICatalogService _catalogService;

    private const string RootFolderPathKey = "RootFolderPath";
    private const string SelectedFolderPathKey = "SelectedFolderPath";
    private const string FileTypeFilterKey = "FileTypeFilter";
    private const string McpServerEnabledKey = "McpServerEnabled";
    private const string McpTokenKey = "McpToken";

    public AppSettingsService(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            var fileTypeFilter = ImageFileTypeFilter.All;
            var savedFilter = await _catalogService.GetAppSettingAsync(FileTypeFilterKey);
            if (!string.IsNullOrEmpty(savedFilter) &&
                Enum.TryParse<ImageFileTypeFilter>(savedFilter, ignoreCase: true, out var parsedFilter))
            {
                fileTypeFilter = parsedFilter;
            }

            var settings = new AppSettings
            {
                RootFolderPath = await _catalogService.GetAppSettingAsync(RootFolderPathKey),
                SelectedFolderPath = await _catalogService.GetAppSettingAsync(SelectedFolderPathKey),
                FileTypeFilter = fileTypeFilter,
                McpServerEnabled = bool.TryParse(
                    await _catalogService.GetAppSettingAsync(McpServerEnabledKey),
                    out var mcpServerEnabled) && mcpServerEnabled,
                McpToken = await _catalogService.GetAppSettingAsync(McpTokenKey)
            };
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await _catalogService.SetAppSettingAsync(RootFolderPathKey, settings.RootFolderPath);
        await _catalogService.SetAppSettingAsync(SelectedFolderPathKey, settings.SelectedFolderPath);
        await _catalogService.SetAppSettingAsync(FileTypeFilterKey, settings.FileTypeFilter.ToString());
        await _catalogService.SetAppSettingAsync(
            McpServerEnabledKey, settings.McpServerEnabled.ToString());
        await _catalogService.SetAppSettingAsync(McpTokenKey, settings.McpToken);
    }
}

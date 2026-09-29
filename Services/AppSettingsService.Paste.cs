using System.Text.Json;

namespace HappyPhoton.Services;

public partial class AppSettingsService
{
    private const string PasteGroupsKey = "PasteGroups";

    private async Task<Dictionary<string, bool>> LoadGroupPreferencesAsync(string key)
    {
        var saved = await _catalogService.GetAppSettingAsync(key);
        if (string.IsNullOrEmpty(saved)) return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(saved) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

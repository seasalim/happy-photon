using System.Text.Json;

namespace HappyPhoton.Services;

public partial class AppSettingsService
{
    private const string PasteGroupsKey = "PasteGroupsV2";

    private async Task<Dictionary<string, bool>> LoadPasteGroupsAsync()
    {
        if (await _catalogService.GetAppSettingAsync(PasteGroupsKey) is not null)
            return await LoadGroupPreferencesAsync(PasteGroupsKey);

        // The new storage key saves the migration marker and choice together.
        var groups = await LoadGroupPreferencesAsync("PasteGroups");
        var adjustments = groups.GetValueOrDefault("Adjustments", true);

        if (groups.GetValueOrDefault("Presence", true) != adjustments)
        {
            groups["Presence"] = adjustments;
        }

        return groups;
    }

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

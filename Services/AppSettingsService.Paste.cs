using System.Text.Json;

namespace HappyPhoton.Services;

public partial class AppSettingsService
{
    private const string PasteGroupsKey = "PasteGroups";

    private async Task<Dictionary<string, bool>> LoadPasteGroupsAsync()
    {
        var saved = await _catalogService.GetAppSettingAsync(PasteGroupsKey);
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

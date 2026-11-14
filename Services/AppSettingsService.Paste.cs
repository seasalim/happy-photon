using System.Text.Json;

namespace HappyPhoton.Services;

public partial class AppSettingsService
{
    private const string PasteGroupsKey = "PasteGroupsV2";

    private static Dictionary<string, bool> LoadPasteGroups(Func<string, string?> read)
    {
        if (read(PasteGroupsKey) is { } saved) return LoadGroupPreferences(saved);

        // The new storage key saves the migration marker and choice together.
        var groups = LoadGroupPreferences(read("PasteGroups"));
        var adjustments = groups.GetValueOrDefault("Adjustments", true);

        if (groups.GetValueOrDefault("Presence", true) != adjustments)
        {
            groups["Presence"] = adjustments;
        }

        return groups;
    }

    private static Dictionary<string, bool> LoadGroupPreferences(string? saved)
    {
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

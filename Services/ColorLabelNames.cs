using System.Text.Json;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public sealed class ColorLabelNames
{
    internal const string SettingKey = "color_label_names";
    private readonly CatalogService _catalogService;

    public static IReadOnlyDictionary<ColorLabel, string> Defaults { get; } =
        new Dictionary<ColorLabel, string>
        {
            [ColorLabel.Red] = "Red",
            [ColorLabel.Yellow] = "Yellow",
            [ColorLabel.Green] = "Green",
            [ColorLabel.Blue] = "Blue",
            [ColorLabel.Purple] = "Purple"
        };

    private static readonly ColorLabel[] Slots = Enum.GetValues<ColorLabel>()
        .Where(label => label != ColorLabel.None)
        .ToArray();

    public ColorLabelNames(CatalogService catalogService) =>
        _catalogService = catalogService;

    public async Task<IReadOnlyDictionary<ColorLabel, string>> LoadAsync()
    {
        var stored = await _catalogService.GetAppSettingAsync(SettingKey);
        if (string.IsNullOrWhiteSpace(stored)) return CopyDefaults();

        try
        {
            var values = JsonSerializer.Deserialize<string[]>(stored);
            if (values == null || values.Length != Slots.Length ||
                values.Any(string.IsNullOrWhiteSpace))
            {
                return CopyDefaults();
            }

            return Slots.Select((label, index) => (label, index)).ToDictionary(
                item => item.label,
                item => values[item.index].Trim());
        }
        catch (JsonException)
        {
            return CopyDefaults();
        }
    }

    public Task SaveAsync(IReadOnlyDictionary<ColorLabel, string> names)
    {
        var values = Slots
            .Select(label => names.TryGetValue(label, out var name) &&
                             !string.IsNullOrWhiteSpace(name)
                ? name.Trim()
                : Defaults.GetValueOrDefault(label, label.ToString()))
            .ToArray();
        return _catalogService.SetAppSettingAsync(
            SettingKey,
            JsonSerializer.Serialize(values));
    }

    private static IReadOnlyDictionary<ColorLabel, string> CopyDefaults() =>
        new Dictionary<ColorLabel, string>(Defaults);
}

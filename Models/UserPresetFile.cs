using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

public class UserPresetFile
{
    public const int CurrentVersion = 2;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("settings")]
    public EditSettings Settings { get; set; } = new();

    public Preset ToPreset() => new(Id, Name, Settings);
}

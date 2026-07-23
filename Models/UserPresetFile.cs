using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

public class UserPresetFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("settings")]
    public EditSettings Settings { get; set; } = new();

    public Preset ToPreset() => new(Id, Name, Settings);
}

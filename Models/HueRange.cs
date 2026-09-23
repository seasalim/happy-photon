using System.Text.Json.Serialization;

namespace HappyPhoton.Models;

public sealed record HueRange
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
    [JsonPropertyName("center")]
    public double Center { get; init; } = 240;
    [JsonPropertyName("width")]
    public double Width { get; init; } = 60;
    [JsonPropertyName("softness")]
    public double Softness { get; init; } = 30;
}
